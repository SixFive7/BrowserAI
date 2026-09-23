// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.IO.Enumeration;
using System.Text;
using System.Text.Json;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The release script: the validation rule the suite can drive, and the
/// packaging decisions that must never quietly change.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule is driven, the flags are scanned, and the difference is
/// deliberate.</b> <c>build/Test-ReleaseVersion.ps1</c> is a pure decision and
/// is executed here for real, both ways. The <c>vpk pack</c> invocation cannot
/// be — it needs the tool, a publish and two minutes — so what is asserted about
/// it is that the four decisions with a blast radius are still in the file:
/// never <c>--msi</c>, the entry executable rather than the stub, a
/// <b>Start Menu</b> entry and no desktop one, and an ILC scan that looks for
/// the one thing that is not a diagnostic. <i>Corrected 2026-09-16 (previously
/// "no shortcuts")</i> — <c>--shortcuts StartMenuRoot</c> has been passed since
/// 2026-09-15, when the main executable became the configuration app and a
/// person needed a way to open it again.
/// </para>
/// <para>
/// <b>What a scan can and cannot do is stated rather than implied.</b> It cannot
/// prove the pack behaves; the
/// [install → update → rollback cycle](../../kb/packaging/velopack.md#install--update--rollback-end-to-end)
/// did
/// that, by hand, against a real installer this suite must never run — an
/// installer renames a non-empty root aside and deletes it, which on a
/// developer's machine is 768 MB of provisioned browsers. What it does do is
/// make a silent edit to any of those four decisions a red build.
/// </para>
/// </remarks>
internal sealed class ReleaseScriptTests
{
    private static string ValidationScript => Path.Combine(RepositoryLayout.Root.FullName, "build", "Test-ReleaseVersion.ps1");

    private static string ReleaseScript => Path.Combine(RepositoryLayout.Root.FullName, "build", "New-Release.ps1");

    private static string ManifestScript => Path.Combine(RepositoryLayout.Root.FullName, "build", "Write-ReleaseManifest.ps1");

    private static string NotesScript => Path.Combine(RepositoryLayout.Root.FullName, "build", "New-ReleaseNotes.ps1");

    private static string IlcPassScript => Path.Combine(RepositoryLayout.Root.FullName, "build", "Test-IlcFullPass.ps1");

    private static string ClearScript => Path.Combine(RepositoryLayout.Root.FullName, "build", "Clear-TestPackFeed.ps1");

    private static string UploadAssetsScript => Path.Combine(RepositoryLayout.Root.FullName, "build", "Set-UploadAssets.ps1");

    /// <summary>An empty channel accepts anything, because there is nothing to be older than.</summary>
    /// <remarks>
    /// The same 404 an unpublished channel returns is what a misconfigured feed
    /// URL returns, so this state is ordinary and has to be expressible.
    /// </remarks>
    [Test]
    public async Task AnEmptyChannelAcceptsTheFirstRelease()
    {
        using var scratch = ScratchDirectory.Create("release-first");
        var (exit, verdict, _) = await RunAsync(ValidationScript, "-Manifest", Path.Combine(scratch.Path, "releases.win.json"), "-Version", "0.1.0");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(verdict.Trim()).IsEqualTo("first");
    }

    /// <summary>A newer version is ordinary and needs nothing said.</summary>
    [Test]
    public async Task AHigherVersionIsMonotonicAndPasses()
    {
        using var scratch = ScratchDirectory.Create("release-monotonic");
        var manifest = await WriteFeedAsync(scratch.Path, "0.9.0");

        var (exit, verdict, _) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.1");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(verdict.Trim()).IsEqualTo("monotonic");
    }

    /// <summary>
    /// A lower version is refused <b>unless it is stated</b> — and this is the
    /// half that must exist, or rollback is a client-side fiction.
    /// </summary>
    /// <remarks>
    /// <b>Both halves or neither works.</b> <c>AllowVersionDowngrade</c> on the
    /// client makes an older version acceptable; a pipeline rule of *strictly
    /// increasing* makes one impossible to publish. A shipping product examined
    /// for this project has exactly that pair and therefore has no rollback at
    /// all, in either direction — which is why the refusal here names the switch
    /// instead of being final.
    /// </remarks>
    [Test]
    public async Task ALowerVersionIsRefusedWithoutRollbackRepublishAndAcceptedWithIt()
    {
        using var scratch = ScratchDirectory.Create("release-rollback");
        var manifest = await WriteFeedAsync(scratch.Path, "0.9.0", "0.9.1");

        var (refusedExit, _, refusedOutput) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.0");

        await Assert.That(refusedExit).IsNotEqualTo(0);
        await Assert.That(refusedOutput).Contains("ROLLBACK");
        await Assert.That(refusedOutput).Contains("-RollbackRepublish");
        await Assert.That(refusedOutput).Contains("AllowVersionDowngrade");

        var (statedExit, statedVerdict, _) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.0", "-RollbackRepublish");

        await Assert.That(statedExit).IsEqualTo(0);
        await Assert.That(statedVerdict.Trim()).IsEqualTo("rollback");
    }

    /// <summary>
    /// A release cut over a local feed still holding this machine's own
    /// pre-release packs is refused, and the refusal names the files to clear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the state the repository is actually in between releases,
    /// and it used to read as a rollback.</b> Every gate that installs a real
    /// installer needs a pack, and a pack is made by running the release script
    /// on whatever MinVer derives from a commit past the tag — so
    /// <c>Releases/</c> accumulates <c>1.0.1-alpha.0.19</c>,
    /// <c>1.0.1-alpha.0.2</c> and a stale feed manifest naming them. Cutting
    /// <c>1.0.0</c> against that is <i>lower than the published version</i>, and
    /// the script said <b>ROLLBACK … re-run with -RollbackRepublish</b> — advice
    /// that would have published a release into a feed whose manifest and
    /// asset list name packages that were never released. <i>Added 2026-09-16.</i>
    /// </para>
    /// <para>
    /// <b>It is narrower than the rollback rule on purpose.</b> It fires only
    /// when the CANDIDATE is a release and the thing above it is a
    /// PRE-RELEASE — which cannot be a published state, because nothing
    /// pre-release is ever uploaded. A genuine rollback over published releases
    /// is untouched, and the arm below is the control that says so.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AReleaseCutOverLocalPreReleasePacksIsRefusedAndNamesWhatToClear()
    {
        using var scratch = ScratchDirectory.Create("release-stale-feed");
        var manifest = await WriteFeedAsync(scratch.Path, "1.0.0", "1.0.1-alpha.0.2", "1.0.1-alpha.0.19");

        var (exit, _, output) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "1.0.0");

        await Assert.That(exit).IsNotEqualTo(0);

        // The version that is in the way, by name.
        await Assert.That(output).Contains("1.0.1-alpha.0.19");

        // And exactly what to clear, so the reader does not have to work it out
        // — including the two directories that must SURVIVE.
        await Assert.That(output).Contains("Releases");
        await Assert.That(output).Contains(".nupkg");
        await Assert.That(output).Contains("releases.win.json");
        await Assert.That(output).Contains("RELEASES");
        await Assert.That(output).Contains("assets.win.json");
        await Assert.That(output).Contains("archive");
        await Assert.That(output).Contains("test-pack");

        // Never the rollback ADVICE, which is the wrong door and would publish a
        // release into a feed naming packages nobody released — and the switch
        // is named anyway, to say so.
        await Assert.That(output.Contains("Re-run with -RollbackRepublish", StringComparison.Ordinal)).IsFalse();
        await Assert.That(output).Contains("Do NOT pass -RollbackRepublish");

        // ---- The controls, in three directions -------------------------------
        // (1) A genuine rollback over published RELEASES still reads as one.
        var released = await WriteFeedAsync(Path.Combine(scratch.Path, "b"), "1.0.0", "1.0.1");
        var (rollbackExit, _, rollbackOutput) = await RunAsync(ValidationScript, "-Manifest", released, "-Version", "1.0.0");

        await Assert.That(rollbackExit).IsNotEqualTo(0);
        await Assert.That(rollbackOutput).Contains("ROLLBACK");

        // (2) A PRE-RELEASE candidate over the same stale feed is the ordinary
        // gate pack and is unaffected: it is newer, so it is monotonic.
        var (gateExit, gateVerdict, _) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "1.0.1-alpha.0.20");

        await Assert.That(gateExit).IsEqualTo(0);
        await Assert.That(gateVerdict.Trim()).IsEqualTo("monotonic");

        // (3) A release cut over a feed holding only OLDER pre-releases is
        // monotonic, because nothing pre-release is above it.
        var behind = await WriteFeedAsync(Path.Combine(scratch.Path, "c"), "0.9.0", "0.9.1-alpha.0.3");
        var (cutExit, cutVerdict, _) = await RunAsync(ValidationScript, "-Manifest", behind, "-Version", "1.0.0");

        await Assert.That(cutExit).IsEqualTo(0);
        await Assert.That(cutVerdict.Trim()).IsEqualTo("monotonic");
    }

    /// <summary>Publishing a version over itself is refused in both directions.</summary>
    [Test]
    public async Task RepublishingTheNewestVersionOverItselfIsRefused()
    {
        using var scratch = ScratchDirectory.Create("release-same");
        var manifest = await WriteFeedAsync(scratch.Path, "0.9.1");

        var (exit, _, output) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.1", "-RollbackRepublish");

        await Assert.That(exit).IsNotEqualTo(0);
        await Assert.That(output).Contains("already the newest release");
    }

    /// <summary>A four-part version and a <c>0.0.0</c> are both refused.</summary>
    /// <remarks>
    /// <c>vpk</c> rejects four-part versions outright, so catching it here is
    /// the difference between a message and a pack failure two minutes into a
    /// publish. <c>0.0.0</c> is what a derivation that found no git tag
    /// produces.
    /// </remarks>
    [Test]
    public async Task AFourPartVersionAndAZeroVersionAreBothRefused()
    {
        using var scratch = ScratchDirectory.Create("release-shape");
        var manifest = Path.Combine(scratch.Path, "releases.win.json");

        var (fourPartExit, _, fourPartOutput) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.1.0");
        await Assert.That(fourPartExit).IsNotEqualTo(0);
        await Assert.That(fourPartOutput).Contains("four-part");

        var (noTagExit, _, noTagOutput) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.0.0-alpha.0.71");
        await Assert.That(noTagExit).IsNotEqualTo(0);
        await Assert.That(noTagOutput).Contains("no git tag");
    }

    /// <summary>
    /// The four packaging decisions with a blast radius, asserted on the script
    /// itself.
    /// </summary>
    /// <remarks>
    /// Each one fails silently if it changes. <c>--msi</c> installs to
    /// <c>Program Files</c> and makes the updater self-elevate, which a
    /// background MCP server cannot answer. The execution stub is
    /// <c>windows_subsystem = "windows"</c> and returns in 59 ms while the app
    /// runs on, so a client registered against it sees its server die instantly.
    /// Shortcuts default to <c>Desktop,StartMenuRoot</c>, which is two entries a
    /// stdio server has no use for. And <i>will always throw</i> is not a
    /// diagnostic, so no MSBuild property can catch it.
    /// </remarks>
    [Test]
    public async Task TheFourPackagingDecisionsAreStillInTheReleaseScript()
    {
        var script = await File.ReadAllTextAsync(ReleaseScript);

        // ⚠️ THE NEGATIVE CHECK IS SCOPED TO THE ARGUMENT ARRAY, and that is not
        // a convenience. The script explains at length WHY --msi is never
        // passed, in a comment block and in a comment inside the array itself,
        // so a whole-file scan matches the explanation and fails -- which trains
        // the next person to delete the reasoning in order to make a test pass.
        // What is asserted is what is passed.
        var start = script.IndexOf("$packArgs = @(", StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(-1);

        var end = script.IndexOf("\n)", start, StringComparison.Ordinal);
        await Assert.That(end).IsGreaterThan(start);

        var passed = string.Join(
            '\n',
            script[start..end].Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

        // Never per-machine: --msi PerMachine installs to Program Files and
        // makes the updater self-elevate.
        await Assert.That(passed).DoesNotContain("--msi");

        // ⚠️ THE MAIN EXE IS THE CONFIGURATION APP AND THE NAME IS UNCHANGED,
        // which is exactly why this line needs the comment: `BrowserAI.exe`
        // meant the server until 2026-09-15 and means the app from that day.
        // One name decides which binary Setup.exe starts after a non-silent
        // install, which one the stub and `Update.exe start` launch, which one
        // all four hooks run on, and what the shortcut points at.
        await Assert.That(passed).Contains("'--mainExe', 'BrowserAI.exe'");

        // ⚠️ Corrected 2026-09-15 (previously "'--shortcuts', 'None'", with the
        // reason "this is a background stdio server that a human never
        // launches"). True of the only binary there was; false of the one the
        // name above now points at. Without an entry the configuration app
        // could be seen exactly once, on the install that started it.
        await Assert.That(passed).Contains("'--shortcuts', 'StartMenuRoot'");

        // Never the default Desktop,StartMenuRoot: a desktop icon for something
        // opened twice a year is clutter, and the default is what arrives if
        // the argument is ever dropped rather than changed.
        await Assert.That(passed).DoesNotContain("Desktop");

        await Assert.That(passed).Contains("'--icon', $icon");

        // And nothing anywhere in the script hands start arguments to the
        // installer: `Setup.exe -- <args>` panics with a downcast failure and
        // NEVER EXITS, installing nothing and leaving one log line.
        await Assert.That(script).DoesNotContain("Setup.exe' --");
        await Assert.That(script).Contains("will always throw");
    }

    /// <summary>
    /// The pack id is what chooses the install directory, and the installer is
    /// renamed back to the name a person downloads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Velopack derives the install location from the pack id and from
    /// nothing else</b> — <c>%LocalAppData%\&lt;packId&gt;</c>, immovable at
    /// 1.2.0: there is no flag for it and an id may not carry a path. So the id
    /// is the only lever there is, and it is what puts the install root at
    /// <c>BrowserAI.app</c> <i>beside</i> the data root at <c>BrowserAI</c>
    /// rather than on top of it. That matters because <c>Setup.exe</c> renames a
    /// non-empty install root aside and deletes it, and uninstall empties it —
    /// which, under the old layout, took 768 MB of provisioned browsers and the
    /// session index with it.
    /// </para>
    /// <para>
    /// <b>The renames are asserted with it, because the two are one
    /// decision.</b> A suffix that exists to answer a question about directories
    /// has no business on a file a person downloads from a releases page, and
    /// neither does vpk's own <c>-Setup</c> / <c>-Portable</c> vocabulary.
    /// ⚠️ <b>Exactly TWO artefacts are renamed since 2026-09-15</b> *(previously
    /// one, the installer, to <c>BrowserAI-win-Setup.exe</c>)*: the installer
    /// becomes <c>BrowserAI.exe</c> and the portable archive
    /// <c>BrowserAI.zip</c>. The <c>.nupkg</c>s keep the id, because Velopack
    /// resolves those by name out of <c>releases.&lt;channel&gt;.json</c> and a
    /// rename there is a feed that 404s on the first update.
    /// </para>
    /// <para>
    /// <b>The channel leaves the name only on the default channel</b>, and that
    /// half is asserted too: two channels packed into one output directory must
    /// not collide, which is the one property vpk's naming had and a plain name
    /// could lose silently.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    /// <summary>
    /// Both binaries are published into one pack directory, each behind its own
    /// ILC gate, and neither may be missing when the pack runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>HALT-A once per publish is the whole point of the loop.</b> Two
    /// binaries are linked into one release by two ILC passes, and a scan that
    /// read one of the two logs would ship a binary nobody had checked while
    /// reporting that ILC's output was clean — which is the same defect the
    /// full-pass check exists for, one level up.
    /// </para>
    /// <para>
    /// <b>The intermediates sweep is asserted with it</b>, because the two are
    /// one mechanism: <c>IlcCompile</c> is skippable when its object file is
    /// newer than its inputs, and a skipped pass leaves a log with nothing of
    /// ILC's in it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task BothBinariesArePublishedIntoOnePackDirectoryEachBehindItsOwnIlcGate()
    {
        var script = await File.ReadAllTextAsync(ReleaseScript);

        await Assert.That(script).Contains("$appProject = Join-Path $root 'src' 'BrowserAI.App' 'BrowserAI.App.csproj'");
        await Assert.That(script).Contains("Exe = 'BrowserAI.exe'");
        await Assert.That(script).Contains("Exe = 'BrowserAI.Server.exe'");

        // The publish, the full-pass refusal and the complaint scan are all
        // inside one loop over $publishes, so neither binary can be the one
        // nobody read a log for.
        var loop = script.IndexOf("foreach ($publish in $publishes) {", StringComparison.Ordinal);

        await Assert.That(loop).IsGreaterThan(-1);

        var publishLoop = script.IndexOf(
            "        Write-Host \"Publishing the $($publish.What) (NativeAOT) to $stage ...\"",
            StringComparison.Ordinal);

        await Assert.That(publishLoop).IsGreaterThan(-1);

        var body = script[publishLoop..];
        var gate = body.IndexOf("Test-IlcFullPass.ps1", StringComparison.Ordinal);
        var complaints = body.IndexOf("$ilcComplaints = $ilc", StringComparison.Ordinal);
        var end = body.IndexOf("\n    }", StringComparison.Ordinal);

        await Assert.That(gate).IsGreaterThan(-1);
        await Assert.That(complaints).IsGreaterThan(gate);
        await Assert.That(end).IsGreaterThan(complaints);

        // One log per binary, named for it. A single shared name would leave
        // the second publish overwriting the first's evidence.
        await Assert.That(script).Contains("[System.IO.Path]::GetFileNameWithoutExtension($publish.Exe)");

        // Both intermediates directories, or one IlcCompile stays skippable.
        await Assert.That(script).Contains("$obj = Join-Path (Split-Path -Parent $publish.Project) 'obj' 'Release'");

        // And the pack refuses a directory holding one of the two.
        await Assert.That(script).Contains("so the $($publish.What) is missing and there is nothing releasable to pack");

        // ⚠️ EACH PUBLISH STAGES INTO A DIRECTORY OF ITS OWN and is copied in
        // afterwards. Measured 2026-09-15: two publishes with `-o` pointed at one
        // directory produced a pack directory holding the server and NOT the
        // app, while leaving the app's `.pdb` behind so it looked populated.
        await Assert.That(script).Contains("$stage = Join-Path $root 'artifacts' (\"publish-\" + [System.IO.Path]::GetFileNameWithoutExtension($publish.Exe))");
        await Assert.That(script).Contains("Copy-Item -Path (Join-Path $stage '*') -Destination $PackDir -Recurse -Force");
    }

    [Test]
    public async Task ThePackIdIsTheInstallDirectoryAndTheDownloadsAreRenamedBack()
    {
        var script = await File.ReadAllTextAsync(ReleaseScript);

        await Assert.That(script).Contains("$packId = 'BrowserAI.app'");
        await Assert.That(script).Contains("$downloadId = 'BrowserAI'");

        // The id reaches vpk, so the install root really is derived from it.
        var start = script.IndexOf("$packArgs = @(", StringComparison.Ordinal);
        var end = script.IndexOf("\n)", start, StringComparison.Ordinal);

        await Assert.That(script[start..end]).Contains("'--packId', $packId");

        // The suffix rule, and the default it is compared against. The parameter
        // default and the literal have to agree, or the plain names would appear
        // on a channel that is not the default one.
        await Assert.That(script).Contains("[string] $Channel = 'win'");
        await Assert.That(script).Contains("$defaultChannel = 'win'");
        await Assert.That(script).Contains("$downloadSuffix = if ($Channel -eq $defaultChannel) { '' } else { \"-$Channel\" }");

        // Both renames, by packed name and by download name, so neither can be
        // dropped without this going red.
        await Assert.That(script).Contains("Packed = \"$packId-$Channel-Setup.exe\";    Download = \"$downloadId$downloadSuffix.exe\"");
        await Assert.That(script).Contains("Packed = \"$packId-$Channel-Portable.zip\"; Download = \"$downloadId$downloadSuffix.zip\"");
        await Assert.That(script).Contains("Move-Item -LiteralPath $packedPath -Destination $downloadPath -Force");

        // And the refusal that stops a rename being a silent no-op: an artefact
        // that was not produced must fail the release rather than leave the
        // previous run's file in place under the right name.
        await Assert.That(script).Contains("so there is no $($download.What) to rename or to publish");
        await Assert.That(script).Contains("Required = $true");
        await Assert.That(script).DoesNotContain("Required = $false");

        // The human-facing manifest directory keeps the VERSION, because it is a
        // record rather than a download -- named for the download id and not for
        // the pack id, and deliberately not flattened to `BrowserAI`.
        await Assert.That(script).Contains("$manifestDir = Join-Path $ArchiveDir \"$downloadId-$PackVersion-manifest\"");

        // And the feed-internal packages do NOT get renamed: these two are the
        // control, and a sweep that renamed everything would fail here rather
        // than in the field on somebody's first update.
        await Assert.That(script).Contains("$full = Join-Path $OutputDir \"$packId-$PackVersion-full.nupkg\"");
        await Assert.That(script).Contains("$delta = Join-Path $OutputDir \"$packId-$PackVersion-delta.nupkg\"");
    }

    /// <summary>
    /// The resolved-set manifest is emitted, holds the seven files item 11
    /// names, and states the version each one carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Driven against a synthetic root rather than the repository's own.</b>
    /// One of the seven is <c>payload/payload.json</c>, which exists only after
    /// <c>build/Build-Payload.ps1</c> has run — so pointing this at the real
    /// root would make the test's own result depend on whether a payload
    /// happened to be assembled, which is the conditional-pass shape the whole
    /// [coverage gate](SuiteCoverageTests.cs) exists to remove. The script's
    /// behaviour is identical either way: it copies seven paths and reads them
    /// back.
    /// </para>
    /// <para>
    /// <b>Read back out of the copies.</b> The manifest states a version only
    /// where it copied a file stating it, which is what makes the number
    /// evidence rather than something somebody typed.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-26 (previously
    /// <c>TheResolvedSetManifestIsEmittedWithAllSixFilesAndWhatEachStates</c>,
    /// over six files).</b> <c>tool-verdicts.json</c> arrived at the repository
    /// root the same week and was left out of the manifest's charter: it states
    /// which tools a build forwards and which upstream versions that judgement
    /// was made against, so a release that cannot produce it cannot answer why
    /// it refused a tool the next release allows.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    /// <summary>
    /// The release body is generated by the release script, from the section
    /// being cut, and travels with the release evidence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Until 2026-09-15 the body was produced by hand at publish time</b> —
    /// the stamped section, cut at whichever heading boundary fell nearest
    /// GitHub's 125,000-character field, plus a permalink line. That is a
    /// document nobody can reproduce afterwards and nothing can check, and the
    /// one that was published opened with four warning icons and seventy screens
    /// of the middle of an argument.
    /// </para>
    /// <para>
    /// <b>What is asserted here is the wiring, and only the wiring.</b> What the
    /// body looks like is `ChangelogTests`' business, over fixtures, where an
    /// exact expected document is readable; running the release script to find
    /// out would cost two NativeAOT publishes. So this holds that the release
    /// script calls the generator, hands it the version it is packing rather
    /// than a version of its own, puts the body where the manifest goes, and
    /// reports it — a release whose body is written somewhere nobody looks is
    /// the same defect wearing a script.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheReleaseBodyIsGeneratedFromTheSectionRatherThanCutFromIt()
    {
        var script = await File.ReadAllTextAsync(ReleaseScript);

        await Assert.That(File.Exists(NotesScript)).IsTrue();

        // The INVOCATION, never the first mention of the name: the comment above
        // it names the script too, and a window measured from a comment is a
        // window that moves whenever somebody explains themselves at more or
        // less length.
        var call = script.IndexOf("& (Join-Path $PSScriptRoot 'New-ReleaseNotes.ps1')", StringComparison.Ordinal);
        await Assert.That(call).IsGreaterThan(-1);

        // The version it packs, never one it works out again: two derivations of
        // one version is how they come to disagree, which is the rule
        // Get-ReleaseNotes.ps1 states about itself.
        var invocation = script[call..Math.Min(script.Length, call + 600)];

        await Assert.That(invocation).Contains("-Version $PackVersion");
        await Assert.That(invocation).Contains("-Destination");

        // The destination is named a few lines above the call rather than on it,
        // so the step is read as a whole: what matters is that the body lands
        // where the manifest does and not that one expression carries both.
        var step = script[Math.Max(0, call - 400)..Math.Min(script.Length, call + 600)];

        await Assert.That(step).Contains("$manifestDir");

        // And a failure stops the release rather than leaving it bodyless.
        await Assert.That(invocation).Contains("if ($LASTEXITCODE -ne 0) { exit 1 }");

        // ⚠️ AND IT IS SKIPPED FOR A PRE-RELEASE VERSION, which is not a defect
        // in the guard but the whole reason this branch exists. `New-Release.ps1`
        // is run twice for every release the suite is part of: once on a real
        // version, and once on `1.0.1-alpha.0.19` or whatever MinVer derives, to
        // produce the pack the capability-gated arms install. The second has no
        // changelog section and never will, and a body step that refused it
        // would make the release script unusable for the thing it is used for
        // most. Found by running it: the pack succeeded and the body step exited
        // 1 naming a section nobody had written.
        // Scoped to the lines immediately above the call rather than to the
        // whole file: the pre-release SUFFIX is already tested four hundred
        // lines earlier, for a different reason, and a whole-file search would
        // pass on that one and assert nothing about this step.
        var guard = script[Math.Max(0, call - 400)..call];

        await Assert.That(guard).Contains("if ($PackVersion -match '-')");
        await Assert.That(guard).Contains("else");

        // Reported, so the shape the size guard chose is in the release record
        // rather than only on somebody's screen.
        var report = script.IndexOf("[pscustomobject]@{", StringComparison.Ordinal);
        await Assert.That(report).IsGreaterThan(call);
        await Assert.That(script[report..]).Contains("ReleaseBody");
        await Assert.That(script[report..]).Contains("ReleaseBodyShape");
    }

    [Test]
    public async Task TheResolvedSetManifestIsEmittedWithAllEightFilesAndWhatEachStates()
    {
        using var scratch = ScratchDirectory.Create("release-manifest");
        var root = await SyntheticRootAsync(scratch.Path);
        var destination = Path.Combine(scratch.Path, "manifest");

        var (exit, _, output) = await RunAsync(
            ManifestScript, "-Root", root, "-Destination", destination, "-Version", "0.9.1", "-Tag", "v0.9.0-3-gabc1234");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(output).Contains("Resolved-set manifest");

        string[] expected =
        [
            "src-BrowserAI.packages.lock.json",
            "tests-BrowserAI.Tests.packages.lock.json",
            "tests-BrowserAI.TestProbe.packages.lock.json",
            "payload.package-lock.json",
            "payload.package.json",
            "payload.json",
            "browsers.json",
            "tool-verdicts.json",
            "manifest.json",
        ];

        var missing = expected.Where(name => !File.Exists(Path.Combine(destination, name))).ToList();
        await Assert.That(string.Join(", ", missing)).IsEmpty();

        // ⚠️ AND NOTHING ELSE — 2026-09-15, when the release script started
        // packing a SECOND installer under a test-only id for the suite to
        // install. That pack must never reach a release: not as an asset, not as
        // a row in the resolved set, not as a file in the directory a person
        // opens a year later to find out what shipped. The manifest is a fixed
        // list of EIGHT copies plus its own `manifest.json`, so this is what
        // turns "it cannot get in by construction" into a red build the day
        // somebody adds a ninth. *(Eight since 2026-09-18, previously seven —
        // `build/payload/package.json` is the only record that an npm override
        // is in force, because npm writes no `overrides` block into the lock it
        // produces.)*
        var extra = Directory.EnumerateFileSystemEntries(destination)
            .Select(Path.GetFileName)
            .Where(name => !expected.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(string.Join(", ", extra)).IsEmpty();

        var manifest = await File.ReadAllTextAsync(Path.Combine(destination, "manifest.json"));

        await Assert.That(manifest).Contains("\"version\": \"0.9.1\"");
        await Assert.That(manifest).Contains("\"tag\": \"v0.9.0-3-gabc1234\"");

        // Every axis the resolved set has to state, read back from the copies.
        await Assert.That(manifest).Contains("\"Velopack\": \"9.9.9\"");
        await Assert.That(manifest).Contains("\"TUnit\": \"8.8.8\"");
        await Assert.That(manifest).Contains("\"@playwright/mcp\": \"0.0.777\"");
        await Assert.That(manifest).Contains("\"version\": \"v24.0.0\"");
        await Assert.That(manifest).Contains("\"revision\": \"4321\"");

        // The verdicts file states what it was judged against, and the manifest
        // states that rather than the row set: which tools a build forwards is
        // only meaningful beside the upstream it was adjudicated on. The
        // synthetic value is one no real resolve could produce.
        await Assert.That(manifest).Contains("\"@playwright/mcp\": \"0.0.778\"");

        // ⚠️ THE PULLED-FORWARD DEPENDENCY, and it is the OPPOSITE of `override`
        // above. `override` is a version a human HELD BACK; this is one the
        // payload ships AHEAD of what the wrapper declares for itself, which is
        // what an npm `overrides` entry does. Both keys are always present, for
        // the same reason: an absent key is not a statement.
        //
        // Every number here is READ rather than typed into the script — the
        // shipped version and the declared ones out of the copied lock, the pin
        // out of the copied `package.json` — so the fixture makes the three
        // differ. A fixture where they agreed could not tell a real read from a
        // value copied off the wrong line.
        await Assert.That(manifest).Contains("\"pulledForward\"");
        await Assert.That(manifest).DoesNotContain("\"pulledForward\": null");
        await Assert.That(manifest).Contains("\"shipped\": \"1.99.0-alpha-2026-01-01\"");
        await Assert.That(manifest).Contains("\"pinnedTo\": \"1.99.0-alpha-2026-01-01\"");
        await Assert.That(manifest).Contains("\"@playwright/mcp\": \"1.99.0-alpha-2025-12-31\"");
        await Assert.That(manifest).Contains("\"playwright\": \"1.99.0-alpha-2025-12-31\"");

        // And the schema text that tells the two fields apart, because a reader
        // a year from now meets `override: null` beside a populated
        // `pulledForward` and has nothing else to go on.
        await Assert.That(manifest).Contains("_pulledForward");
        await Assert.That(manifest).Contains("PULLED FORWARD");
    }

    /// <summary>
    /// A payload with no override in force yields the empty value rather than
    /// omitting the field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The control the populated arm above cannot be, and it is the same
    /// argument <c>override</c> already won.</b> A manifest that carries
    /// <c>pulledForward</c> only when there is something to say is one where an
    /// absent key means <i>nothing was pulled forward</i> and <i>this manifest
    /// was written by a build that could not say</i> at once — and every
    /// manifest written before 2026-09-18 is the second. <c>"pulledForward":
    /// null</c> is a statement; an absent key is not.
    /// </para>
    /// <para>
    /// ⚠️ <b>Since 2026-09-21 THIS IS THE REAL CASE and the populated arm above
    /// is the fixture, which is the reverse of how the pair was written on
    /// 2026-09-18.</b> The
    /// <see href="../../../DECISIONS.md">dated <c>playwright-core</c>
    /// exception</see> ended that day when <c>@playwright/mcp</c> 0.0.82 shipped
    /// the fix it was taken for; <c>build/payload/package.json</c> carries no
    /// <c>overrides</c> block, so every real release from here on writes
    /// <c>"pulledForward": null</c> and this arm is the one describing what
    /// ships. <b>Neither arm reads the repository's own payload</b> — both build
    /// a synthetic root — so the retirement did not move either of them, and
    /// that is the property worth stating rather than the coincidence: an arm
    /// that went green because the tree stopped carrying an override would be
    /// an arm measuring the tree instead of the script.
    /// </para>
    /// <para>
    /// <b>The fixture differs from the populated one in exactly one file.</b>
    /// The lock still records a <c>playwright-core</c> whose version differs
    /// from what its dependants declare — because that is what a lock looks like
    /// whenever npm has resolved a range — so what is being asserted is that the
    /// claim comes from <c>build/payload/package.json</c>'s <c>overrides</c> and
    /// from nothing else. Read the field off the lock alone and this arm goes
    /// red.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APayloadWithNoOverrideInForceSaysSoRatherThanSayingNothing()
    {
        using var scratch = ScratchDirectory.Create("release-manifest-no-override");
        var root = await SyntheticRootAsync(scratch.Path, withOverride: false);
        var destination = Path.Combine(scratch.Path, "manifest");

        var (exit, _, _) = await RunAsync(
            ManifestScript, "-Root", root, "-Destination", destination, "-Version", "0.9.1", "-Tag", "v0.9.0-3-gabc1234");

        await Assert.That(exit).IsEqualTo(0);

        var manifest = await File.ReadAllTextAsync(Path.Combine(destination, "manifest.json"));

        await Assert.That(manifest).Contains("\"pulledForward\": null");
        await Assert.That(manifest).DoesNotContain("\"shipped\"");
        await Assert.That(manifest).DoesNotContain("\"pinnedTo\"");

        // The eighth file is copied either way: it is what SAYS no override is
        // in force, so a release that omitted it when there was nothing to
        // report would be unable to tell that from a release that never looked.
        await Assert.That(File.Exists(Path.Combine(destination, "payload.package.json"))).IsTrue();
    }

    /// <summary>
    /// The release script forces a full ILC pass, and the check that says so can
    /// fail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>HALT-A had a premise nothing checked: that ILC ran.</b>
    /// <c>IlcCompile</c> is an MSBuild target with <c>Inputs</c> and
    /// <c>Outputs</c>, so a publish whose managed assemblies have not moved
    /// skips it and relinks the previous run's native object. The publish
    /// succeeds, the binary is good, and the ILC-output scan then sweeps a log
    /// ILC never wrote — <b>a check that cannot fail, reporting clean</b>.
    /// Measured 2026-09-15 at <c>-v:normal</c> over this project: <b>95 lines</b>
    /// with the pass and <b>75</b> without, <c>Generating native code</c> present
    /// in the first and absent in the second.
    /// </para>
    /// <para>
    /// <b>The marker rather than the line count.</b> A count is a property of the
    /// verbosity, the project and the SDK at once, and the one change it would
    /// not survive is a publish that legitimately prints more — which is the
    /// direction this is meant to tolerate. <c>Generating native code</c> is
    /// ILC's own line and is printed when, and only when, the compilation
    /// happens.
    /// </para>
    /// <para>
    /// <b>Both directions, driven for real.</b> The refusal is the half that
    /// matters and it is fed a log carrying the skip message — the positive
    /// control the release script cannot give itself, because a script that
    /// always publishes cleanly can never demonstrate its own refusal.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APublishLogWithNoIlcPassIsRefusedAndOneWithAPassIsAccepted()
    {
        using var scratch = ScratchDirectory.Create("release-ilc");

        var full = Path.Combine(scratch.Path, "full.log");
        var incremental = Path.Combine(scratch.Path, "incremental.log");

        // The shapes MSBuild writes at -v:normal, reduced to the lines that
        // decide. Synthetic on purpose: a fixture copied from a real publish
        // would carry this machine's paths and this week's SDK version.
        await File.WriteAllTextAsync(full, string.Join(
            '\n',
            "  Determining projects to restore...",
            "      IlcCompile:",
            "        Generating native code",
            "        \"C:\\ilc\" @\"obj\\Release\\net10.0-windows\\win-x64\\native\\BrowserAI.ilc.rsp\"",
            "  Build succeeded."));

        await File.WriteAllTextAsync(incremental, string.Join(
            '\n',
            "  Determining projects to restore...",
            "       Skipping target \"IlcCompile\" because all output files are up-to-date with respect to the input files.",
            "  Build succeeded."));

        var (accepted, _, acceptedOutput) = await RunAsync(IlcPassScript, "-Log", full);

        await Assert.That(accepted).IsEqualTo(0);
        await Assert.That(acceptedOutput).Contains("full pass");

        var (refused, _, refusedOutput) = await RunAsync(IlcPassScript, "-Log", incremental);

        await Assert.That(refused).IsNotEqualTo(0);
        await Assert.That(refusedOutput).Contains("did not compile");
        await Assert.That(refusedOutput).Contains("IlcCompile");
        await Assert.That(refusedOutput).Contains("native");

        // And a log that carries neither marker is refused too, rather than
        // being read as a pass: an absent log and an incremental one are the
        // same absence of evidence.
        var silent = Path.Combine(scratch.Path, "silent.log");

        await File.WriteAllTextAsync(silent, "  Build succeeded.");

        var (quiet, _, _) = await RunAsync(IlcPassScript, "-Log", silent);

        await Assert.That(quiet).IsNotEqualTo(0);
    }

    /// <summary>
    /// The release script clears the ILC intermediates before it publishes, and
    /// asks whether the pass happened before it reads the pass's output.
    /// </summary>
    /// <remarks>
    /// <b>The order is the property.</b> Clearing <c>$PackDir</c> was already
    /// there and is not enough: the up-to-date check is on
    /// <c>obj\…\native\BrowserAI.obj</c>, which lives nowhere near the output
    /// directory. There is no MSBuild property that disables that check — the
    /// object file <i>is</i> the check — so the removal is the only lever, and a
    /// scan is what keeps it from being deleted as a slow step nobody could
    /// explain.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheReleaseScriptRemovesTheIlcIntermediatesAndThenRequiresAFullPass()
    {
        var script = await File.ReadAllTextAsync(ReleaseScript);

        var cleared = script.IndexOf("-Filter 'native' -Recurse -Directory", StringComparison.Ordinal);
        var published = script.IndexOf("& dotnet publish @publishArgs", StringComparison.Ordinal);
        var checked_ = script.IndexOf("Test-IlcFullPass.ps1", StringComparison.Ordinal);
        var scanned = script.IndexOf("$ilcComplaints = $ilc", StringComparison.Ordinal);

        await Assert.That(cleared).IsGreaterThan(-1);
        await Assert.That(published).IsGreaterThan(cleared);
        await Assert.That(checked_).IsGreaterThan(published);
        await Assert.That(scanned).IsGreaterThan(checked_);
    }

    /// <summary>
    /// The suite's installer is packed under a test-only id, into a directory of
    /// its own, and the id is the only thing that differs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Velopack writes one Add/Remove Programs key per pack id per user,
    /// named for the id and never for the location.</b> An install under
    /// <c>--installto</c> still rewrites <c>HKCU\…\Uninstall\&lt;packId&gt;</c> to
    /// point at the scratch root, and <c>Update.exe uninstall</c> from that root
    /// calls <c>delete_subkey_all(&lt;id&gt;)</c> unconditionally — no comparison
    /// against <c>InstallLocation</c> anywhere. So an installer arm packed under
    /// the shipping id destroys a real install's entry, and a run killed
    /// part-way leaves it gone with nothing to restore it. Measured on this
    /// machine: no <c>BrowserAI.app</c> key after six installer-arm runs.
    /// </para>
    /// <para>
    /// <b>Built rather than retyped</b>, so a packing decision added to
    /// <c>$packArgs</c> reaches both packs. The suite would otherwise be
    /// exercising an installer built differently from the one that ships, which
    /// is the failure this whole arm exists to avoid rather than to introduce.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSuitesInstallerIsPackedUnderATestIdIntoADirectoryOfItsOwn()
    {
        var script = await File.ReadAllTextAsync(ReleaseScript);

        await Assert.That(script).Contains("$testPackId = 'BrowserAI.app.test'");
        await Assert.That(script).Contains("$testDownloadId = 'BrowserAI.test'");
        await Assert.That(script).Contains("$testOutputDir = Join-Path $OutputDir 'test-pack'");

        // The id really is the shipping id plus a suffix, so the two cannot be
        // pointed at unrelated packages by an edit to one of them.
        await Assert.That(ReleaseLayout.TestPackId).IsEqualTo(ReleaseLayout.PackId + ".test");

        // ⚠️ AND THE TITLE, which is a SECOND name and not a cosmetic one:
        // Velopack names the Start Menu shortcut `<title>.lnk` and the uninstall
        // removes shortcuts by target, so two packs under one title share one
        // `.lnk` and the suite's uninstall deletes the real install's entry.
        // Both literals are asserted here rather than only their inequality,
        // because "they differ" is satisfied by renaming the shipping one.
        await Assert.That(script).Contains("$packTitle = 'BrowserAI'");
        await Assert.That(script).Contains("$testPackTitle = 'BrowserAI (suite)'");
        await Assert.That(ReleaseLayout.TestPackTitle).IsNotEqualTo(ReleaseLayout.PackTitle);

        // And the arg list takes the title from the variable, so replacing the
        // variable really does replace what vpk is handed.
        await Assert.That(script).Contains("'--packTitle', $packTitle");

        // The second pack is the first one's arguments with three elements
        // replaced, and nothing else.
        var built = script.IndexOf("$testPackArgs = @()", StringComparison.Ordinal);

        await Assert.That(built).IsGreaterThan(-1);

        var loop = script[built..script.IndexOf("Write-Host \"vpk $($testPackArgs -join ' ')\"", StringComparison.Ordinal)];

        await Assert.That(loop).Contains("'--packId'");
        await Assert.That(loop).Contains("$testPackId");
        await Assert.That(loop).Contains("'--packTitle'");
        await Assert.That(loop).Contains("$testPackTitle");
        await Assert.That(loop).Contains("'--outputDir'");
        await Assert.That(loop).Contains("$testOutputDir");

        // ⚠️ AND THE SECOND FEED IS CLEARED BEFORE IT IS PACKED INTO, which is
        // the ORDER rather than the call: a clear that ran after the pack would
        // delete the installer the suite is about to run, and one that never ran
        // lets a gate's pre-release refuse the next release cut — after the
        // shipping artifacts have already been built. What the step DOES is
        // driven for real in
        // TheSecondFeedIsClearedBeforeItIsPackedIntoAndNothingElseIs; what can
        // only be read here is where it sits in this file.
        var clear = script.IndexOf("Clear-TestPackFeed.ps1", StringComparison.Ordinal);
        var testPack = script.IndexOf("& vpk @testPackArgs", StringComparison.Ordinal);

        await Assert.That(clear).IsGreaterThan(-1);
        await Assert.That(testPack).IsGreaterThan(-1);
        await Assert.That(clear)
            .IsLessThan(testPack)
            .Because("clearing the feed after packing into it would delete the installer the suite runs");

        // It is handed the same three names the pack is, so the two cannot be
        // pointed at different packages by an edit to one of them.
        await Assert.That(script).Contains("-Directory $testOutputDir -PackId $testPackId -DownloadId $testDownloadId -Channel $Channel");

        // And the names it lands under cannot be mistaken for release artifacts.
        await Assert.That(script).Contains("Download = \"$testDownloadId-installer.exe\"");
        await Assert.That(script).Contains("Download = \"$testDownloadId-portable.zip\"");

        // The published artifacts are still exactly two and still named for a
        // person, which is the half this must not have disturbed.
        await Assert.That(script).Contains("Packed = \"$packId-$Channel-Setup.exe\";    Download = \"$downloadId$downloadSuffix.exe\"");
    }

    /// <summary>
    /// <b>The second feed is cleared before it is packed into, so a stale test
    /// pack can never refuse a release again.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is not hygiene; it is the thing that refused a cut.</b>
    /// <c>Releases/test-pack/</c> is a <i>second Velopack feed</i>, and every
    /// gate pack writes a pre-release into it. At the moment a release is cut it
    /// therefore holds versions newer than the release, and <c>vpk</c> refuses
    /// it exactly the way it refuses one in <c>Releases/</c> - <i>"There is a
    /// release in channel win which is equal or greater to the current version
    /// 1.0.0"</i>. Because the running order packs the shipping artifacts first,
    /// the refusal fires <b>after</b> the real pack has succeeded, so a non-zero
    /// exit names the suite's installer while the release itself is already on
    /// disk. It refused the 2026-09-16 cut in precisely that shape. <b>Q200</b>,
    /// decided 2026-09-17: the script clears its own regenerated,
    /// never-published output rather than a checklist item asking a human to.
    /// </para>
    /// <para>
    /// <b>Driven rather than scanned, which is why the step is its own script.</b>
    /// The <c>vpk pack</c> around it cannot be executed here - it needs the tool,
    /// a publish and two minutes - but a deletion over a directory of files can
    /// be, and a <c>Contains</c> over the script text would assert that a line
    /// was written rather than that it removes anything. So
    /// <c>build/Clear-TestPackFeed.ps1</c> is separate for the same reason
    /// <c>Test-ReleaseVersion.ps1</c> and <c>Write-ReleaseManifest.ps1</c> are,
    /// and <see cref="TheSuitesInstallerIsPackedUnderATestIdIntoADirectoryOfItsOwn"/>
    /// holds that <c>New-Release.ps1</c> actually calls it, before the pack.
    /// </para>
    /// <para>
    /// ⚠️ <b>The control runs in the other direction and is half the point.</b>
    /// A step that cleared the shipping feed would turn a release into a feed
    /// with no history, and one that cleared <c>archive/</c> would delete the
    /// only rollback target this project keeps. So the layout planted here holds
    /// all three - the shipping feed, <c>archive/</c>, and the test feed - and
    /// every file in the first two is asserted <b>present and byte-identical</b>
    /// afterwards. A third direction is planted too: a file under
    /// <c>test-pack/</c> that no pack regenerates survives, because this step
    /// clears what it is about to rewrite and is not a directory wipe.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSecondFeedIsClearedBeforeItIsPackedIntoAndNothingElseIs()
    {
        using var scratch = ScratchDirectory.Create("release-clear-test-feed");

        var shipping = Path.Combine(scratch.Path, "Releases");
        var archive = Path.Combine(shipping, "archive");
        var testPack = Path.Combine(shipping, "test-pack");

        _ = Directory.CreateDirectory(archive);
        _ = Directory.CreateDirectory(testPack);

        // The stale test feed, in the shape a gate pack leaves it: both package
        // kinds, both Velopack manifests, the legacy RELEASES file, the asset
        // list, and the two artifacts AFTER the rename the script performs.
        string[] cleared =
        [
            Path.Combine(testPack, $"{ReleaseLayout.TestPackId}-1.0.1-alpha.0.19-full.nupkg"),
            Path.Combine(testPack, $"{ReleaseLayout.TestPackId}-1.0.1-alpha.0.19-delta.nupkg"),
            Path.Combine(testPack, $"releases.{ReleaseLayout.Channel}.json"),
            Path.Combine(testPack, "RELEASES"),
            Path.Combine(testPack, $"assets.{ReleaseLayout.Channel}.json"),
            Path.Combine(testPack, $"{ReleaseLayout.TestDownloadId}-installer.exe"),
            Path.Combine(testPack, $"{ReleaseLayout.TestDownloadId}-portable.zip"),

            // And the pre-rename names, because a run that died between the pack
            // and the rename leaves these instead - and vpk reads the .nupkg
            // rather than the renamed exe, so either shape refuses the next cut.
            Path.Combine(testPack, $"{ReleaseLayout.TestPackId}-{ReleaseLayout.Channel}-Setup.exe"),
            Path.Combine(testPack, $"{ReleaseLayout.TestPackId}-{ReleaseLayout.Channel}-Portable.zip"),
        ];

        foreach (var file in cleared)
        {
            await File.WriteAllTextAsync(file, "stale");
        }

        // What must survive, in three directions.
        string[] survivors =
        [
            // (1) The shipping feed: its packages, its manifests, its artifacts.
            Path.Combine(shipping, $"{ReleaseLayout.PackId}-1.0.0-full.nupkg"),
            Path.Combine(shipping, $"releases.{ReleaseLayout.Channel}.json"),
            Path.Combine(shipping, "RELEASES"),
            Path.Combine(shipping, $"assets.{ReleaseLayout.Channel}.json"),
            Path.Combine(shipping, $"{ReleaseLayout.DownloadId}.exe"),

            // (2) archive/, which is the only rollback target this project keeps.
            Path.Combine(archive, $"{ReleaseLayout.PackId}-1.0.0-full.nupkg"),

            // (3) Something under test-pack/ that no pack regenerates. This step
            //     clears what it is about to rewrite; it is not a wipe.
            Path.Combine(testPack, "a-note-somebody-left.txt"),
        ];

        foreach (var file in survivors)
        {
            await File.WriteAllTextAsync(file, $"keep {Path.GetFileName(file)}");
        }

        var (exit, _, output) = await RunAsync(
            ClearScript,
            "-Directory", testPack,
            "-PackId", ReleaseLayout.TestPackId,
            "-DownloadId", ReleaseLayout.TestDownloadId,
            "-Channel", ReleaseLayout.Channel);

        await Assert.That(exit).IsEqualTo(0);

        var stillThere = cleared.Where(File.Exists).Select(Path.GetFileName).ToArray();

        await Assert.That(string.Join(", ", stillThere!))
            .IsEqualTo(string.Empty)
            .Because("every file the next pack regenerates must be gone before vpk looks at the feed");

        foreach (var file in survivors)
        {
            await Assert.That(File.Exists(file))
                .IsTrue()
                .Because($"{file} is not the test pack's regenerated output and nothing may delete it");

            await Assert.That(await File.ReadAllTextAsync(file))
                .IsEqualTo($"keep {Path.GetFileName(file)}")
                .Because("a survivor must be untouched rather than merely present");
        }

        // It says what it did, because a silent deletion in a release script is
        // indistinguishable from a step that did not run.
        await Assert.That(output).Contains("test-pack");

        // ---- And it is not a refusal when there is nothing to clear ----------
        // The FIRST cut on a machine meets an empty directory, and a step that
        // failed there would refuse every release on a fresh clone.
        var empty = Path.Combine(scratch.Path, "empty", "test-pack");
        _ = Directory.CreateDirectory(empty);

        var (emptyExit, _, _) = await RunAsync(
            ClearScript,
            "-Directory", empty,
            "-PackId", ReleaseLayout.TestPackId,
            "-DownloadId", ReleaseLayout.TestDownloadId,
            "-Channel", ReleaseLayout.Channel);

        await Assert.That(emptyExit).IsEqualTo(0);

        // Nor when the directory does not exist at all, which is the state
        // before the first gate pack ever runs.
        var (absentExit, _, _) = await RunAsync(
            ClearScript,
            "-Directory", Path.Combine(scratch.Path, "never", "test-pack"),
            "-PackId", ReleaseLayout.TestPackId,
            "-DownloadId", ReleaseLayout.TestDownloadId,
            "-Channel", ReleaseLayout.Channel);

        await Assert.That(absentExit).IsEqualTo(0);
    }

    /// <summary>
    /// What an Add/Remove entry is, decided from its recorded location: a real
    /// install is never refused and a leftover of the suite's own always is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Over constructed inputs, and that is not a shortcut.</b> Planting a
    /// real <c>HKCU\…\Uninstall\…</c> key to exercise the refusal would be the
    /// suite doing the exact thing the refusal exists to prevent — writing an
    /// uninstall entry outside the one its own scratch install creates. So the
    /// classification is asserted here and the reading of the key is the single
    /// line that touches the registry.
    /// </para>
    /// <para>
    /// <b>The REAL direction is the one that matters on this machine.</b> The
    /// maintainer has an install; a capability that judged the shipping id would
    /// redden every run here. The capability judges the <b>test</b> id instead,
    /// where every key is one the suite wrote, so any key that outlives a run is
    /// a run that did not clean up — and the witness names it and the one line
    /// that clears it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARealInstallIsNeverDanglingAndTheSuitesOwnLeftoversAlwaysAre()
    {
        using var scratch = ScratchDirectory.Create("release-arp");
        var elsewhere = ScratchDirectory.Create("release-arp-real");

        using (elsewhere)
        {
            // No key: a clean machine, and the only state that is not a verdict
            // about a directory.
            await Assert.That(ReleaseLayout.Judge(null, scratch.Path))
                .IsEqualTo(ReleaseLayout.UninstallKeyState.Absent);

            // A location that is gone. This is what Velopack leaves when an
            // uninstall removed the tree and something interrupted the key's own
            // removal — Settings then shows an entry for nothing.
            await Assert.That(ReleaseLayout.Judge(Path.Combine(scratch.Path, "went-away"), scratch.Path))
                .IsEqualTo(ReleaseLayout.UninstallKeyState.Dangling);

            // A key with no location at all reads the same way, rather than
            // being read as an install somewhere unknown.
            await Assert.That(ReleaseLayout.Judge(string.Empty, scratch.Path))
                .IsEqualTo(ReleaseLayout.UninstallKeyState.Dangling);

            // A location that EXISTS and is under the suite's scratch root is
            // still the suite's: an arm that was killed between the install and
            // the uninstall leaves exactly this.
            await Assert.That(ReleaseLayout.Judge(scratch.Path, scratch.Path))
                .IsEqualTo(ReleaseLayout.UninstallKeyState.Dangling);

            // ⚠️ AND THE ONE THAT MUST NOT BE REFUSED: a directory that exists
            // and is not the suite's. On this machine that is
            // %LocalAppData%\BrowserAI.app, and reading it as dangling would
            // stop every run.
            await Assert.That(ReleaseLayout.Judge(elsewhere.Path, scratch.Path))
                .IsEqualTo(ReleaseLayout.UninstallKeyState.Real);
        }

        // The witness names the key and the command, so a machine that has to be
        // cleaned by hand is told how in the coverage block rather than in a
        // commit message somebody has to find.
        await Assert.That(ReleaseLayout.TestUninstallKey).Contains(ReleaseLayout.TestPackId);
        await Assert.That(ReleaseLayout.ClearTheLeftoverKey).Contains("reg delete");
        await Assert.That(ReleaseLayout.ClearTheLeftoverKey).Contains(ReleaseLayout.TestUninstallKey);

        // And it judges the TEST id and nothing else: the shipping id never
        // appears in the key this capability reads.
        await Assert.That(ReleaseLayout.TestUninstallKey.EndsWith(ReleaseLayout.PackId, StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// A missing file refuses the manifest rather than writing a partial one.
    /// </summary>
    /// <remarks>
    /// <b>A manifest holding seven of eight files reads exactly like a complete
    /// one</b> to whoever opens it a year later, which makes a partial worse
    /// than none. The refusal names the file, because the usual cause is that
    /// nobody assembled a payload. *(Seven of eight since 2026-09-18, previously
    /// six of seven and five of six before that.)*
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AMissingFileRefusesTheManifestRatherThanWritingAPartialOne()
    {
        using var scratch = ScratchDirectory.Create("release-manifest-partial");
        var root = await SyntheticRootAsync(scratch.Path);
        File.Delete(Path.Combine(root, "payload", "payload.json"));

        var destination = Path.Combine(scratch.Path, "manifest");

        var (exit, _, output) = await RunAsync(
            ManifestScript, "-Root", root, "-Destination", destination, "-Version", "0.9.1");

        await Assert.That(exit).IsNotEqualTo(0);
        await Assert.That(output).Contains("payload/payload.json");
        await Assert.That(output).Contains("Build-Payload.ps1");
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    /// <summary>
    /// The manifest states whether the release was a crunch override, in both
    /// directions — and an ordinary release says <c>null</c> rather than saying
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>No manifest could express an override until 2026-08-26, while two
    /// documents said one did.</b> <c>DECISIONS.md</c> put it in bold — <i>"A
    /// release whose manifest does not say it was overridden is a release
    /// claiming it was not"</i> — and <c>RELEASING.md</c> item 1 said the same;
    /// the script emitted <c>version</c>, <c>tag</c>, <c>package</c>,
    /// <c>sha256</c> and the resolved versions read out of seven copied files,
    /// and had no field for it. By that sentence's own logic every release
    /// claimed it was not overridden, <b>including one that was</b>.
    /// </para>
    /// <para>
    /// <b>The field is always present, and that is the half that makes the claim
    /// true.</b> An absent key is not a statement; <c>"override": null</c> is.
    /// A reader a year later can tell <i>this release says it was not
    /// overridden</i> from <i>this manifest was written by a build that could not
    /// say</i>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheManifestStatesWhetherTheReleaseWasACrunchOverride()
    {
        using var scratch = ScratchDirectory.Create("release-manifest-override");
        var root = await SyntheticRootAsync(scratch.Path);

        var ordinary = Path.Combine(scratch.Path, "ordinary");

        var (plainExit, _, _) = await RunAsync(
            ManifestScript, "-Root", root, "-Destination", ordinary, "-Version", "0.9.1", "-Tag", "v0.9.0-3-gabc1234");

        await Assert.That(plainExit).IsEqualTo(0);

        var plain = await File.ReadAllTextAsync(Path.Combine(ordinary, "manifest.json"));

        await Assert.That(plain).Contains("\"override\": null");

        // And the overridden one, carrying every fact RELEASING.md's evidence
        // line asks for: what was held, at what version, against what newest,
        // why, and who decided.
        var overridden = Path.Combine(scratch.Path, "overridden");

        var (overrideExit, _, _) = await RunAsync(
            ManifestScript,
            "-Root", root,
            "-Destination", overridden,
            "-Version", "0.9.1",
            "-Tag", "v0.9.0-3-gabc1234",
            "-OverriddenPackage", "@playwright/mcp",
            "-OverrideHeldAt", "0.0.700",
            "-OverrideNewest", "0.0.777",
            "-OverrideReason", "0.0.777 renamed browser_click and the release could not wait",
            "-OverrideDecidedBy", "the maintainer");

        await Assert.That(overrideExit).IsEqualTo(0);

        var stated = await File.ReadAllTextAsync(Path.Combine(overridden, "manifest.json"));

        await Assert.That(stated).DoesNotContain("\"override\": null");
        await Assert.That(stated).Contains("\"package\": \"@playwright/mcp\"");
        await Assert.That(stated).Contains("\"heldAt\": \"0.0.700\"");
        await Assert.That(stated).Contains("\"newest\": \"0.0.777\"");
        await Assert.That(stated).Contains("renamed browser_click");
        await Assert.That(stated).Contains("\"decidedBy\": \"the maintainer\"");
    }

    /// <summary>
    /// An override stated in part is refused, naming what is missing.
    /// </summary>
    /// <remarks>
    /// <b>The same argument as the missing-file refusal, applied to the claim
    /// rather than to the evidence.</b> A manifest saying <i>held at 0.0.700</i>
    /// with no newest version, no reason and nobody's name reads, a year later,
    /// exactly like a complete account of the decision — so it refuses rather
    /// than writing one.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnOverrideStatedInPartRefusesTheManifestRatherThanWritingHalfAClaim()
    {
        using var scratch = ScratchDirectory.Create("release-manifest-half-override");
        var root = await SyntheticRootAsync(scratch.Path);
        var destination = Path.Combine(scratch.Path, "manifest");

        var (exit, _, output) = await RunAsync(
            ManifestScript,
            "-Root", root,
            "-Destination", destination,
            "-Version", "0.9.1",
            "-OverriddenPackage", "@playwright/mcp",
            "-OverrideHeldAt", "0.0.700");

        await Assert.That(exit).IsNotEqualTo(0);

        // It names what is missing, because the person running this is the one
        // who took the decision and knows all five answers.
        await Assert.That(output).Contains("OverrideNewest");
        await Assert.That(output).Contains("OverrideReason");
        await Assert.That(output).Contains("OverrideDecidedBy");
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    /// <summary>The release script emits the manifest rather than leaving it to a person.</summary>
    /// <remarks>
    /// The scan is what ties the two together: the test above proves the script
    /// works, and this proves a release runs it. Item 11 was satisfied by hand
    /// once, which is the state this closes.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheReleaseScriptEmitsTheResolvedSetManifest()
    {
        var script = await File.ReadAllTextAsync(ReleaseScript);

        await Assert.That(script).Contains("Write-ReleaseManifest.ps1");
        await Assert.That(script).Contains("-Package $full");
    }

    /// <summary>
    /// A repository-shaped tree holding the eight files, with versions no real
    /// resolve could produce so that a copy cannot be mistaken for a default.
    /// </summary>
    /// <param name="directory">The scratch directory to build the tree under.</param>
    /// <param name="withOverride">
    /// Whether <c>build/payload/package.json</c> carries an npm
    /// <c>overrides</c> entry. Both states are real — one is the payload as it
    /// stands today, the other is every payload before 2026-09-17 and every one
    /// after the override's exit fires.
    /// </param>
    /// <returns>The root of the synthetic tree.</returns>
    private static async Task<string> SyntheticRootAsync(string directory, bool withOverride = true)
    {
        var root = Path.Combine(directory, "root");

        async Task writeAsync(string relative, string content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }

        const string ProductLock = """
            {"version":1,"dependencies":{"net10.0-windows":{
              "ModelContextProtocol":{"type":"Direct","resolved":"7.7.7"},
              "Velopack":{"type":"Direct","resolved":"9.9.9"},
              "MinVer":{"type":"Direct","resolved":"6.6.6"}}}}
            """;

        await writeAsync("src/BrowserAI/packages.lock.json", ProductLock);
        await writeAsync("tests/BrowserAI.Tests/packages.lock.json", """
            {"version":1,"dependencies":{"net10.0-windows":{"TUnit":{"type":"Direct","resolved":"8.8.8"}}}}
            """);
        await writeAsync("tests/BrowserAI.TestProbe/packages.lock.json", """
            {"version":1,"dependencies":{"net10.0-windows":{}}}
            """);
        // ⚠️ THE EMPTY-STRING KEY IS THE POINT OF THIS FIXTURE, not noise. npm
        // records the root project under "" and PowerShell's ConvertFrom-Json
        // refuses an empty property name without -AsHashtable. A fixture without
        // it passed this test while the script failed on the first real release,
        // 2026-08-16 -- a synthetic input simpler than the real one, which is the
        // shape of test that proves nothing.
        //
        // ⚠️ AND THE THREE playwright-core NUMBERS ARE DELIBERATELY DIFFERENT.
        // The version that SHIPS is the lock's own `node_modules/playwright-core`
        // entry; what the wrapper and `playwright` DECLARE for themselves is a
        // day earlier; and the pin that caused it sits in `package.json` rather
        // than anywhere in this file, because npm writes no `overrides` block
        // into the lock it produces. A fixture carrying one number could not tell
        // a manifest that read all three from one that read the first and
        // repeated it.
        await writeAsync("build/payload/package-lock.json", """
            {"name":"payload","lockfileVersion":3,"packages":{
              "":{"name":"payload","dependencies":{"@playwright/mcp":"latest"}},
              "node_modules/@playwright/mcp":{"version":"0.0.777",
                "dependencies":{"playwright":"1.99.0-alpha-2025-12-31","playwright-core":"1.99.0-alpha-2025-12-31"}},
              "node_modules/playwright":{"version":"1.99.0-alpha-2025-12-31",
                "dependencies":{"playwright-core":"1.99.0-alpha-2025-12-31"}},
              "node_modules/playwright-core":{"version":"1.99.0-alpha-2026-01-01"}}}
            """);
        // ⚠️ THE ONLY RECORD THAT AN OVERRIDE IS IN FORCE, which is why it is
        // copied into the manifest rather than read and discarded. The `//`
        // comment key is npm's own convention and the real file carries two of
        // them, so the fixture carries one: a parser that chokes on it would
        // choke on the payload's.
        await writeAsync("build/payload/package.json", withOverride
            ? """
            {"name":"payload","version":"0.0.0","private":true,
             "//":["a note npm ignores"],
             "dependencies":{"@playwright/mcp":"latest"},
             "overrides":{"playwright-core":"1.99.0-alpha-2026-01-01"}}
            """
            : """
            {"name":"payload","version":"0.0.0","private":true,
             "//":["a note npm ignores"],
             "dependencies":{"@playwright/mcp":"latest"}}
            """);
        await writeAsync("payload/payload.json", """
            {"node":{"version":"v24.0.0","lts":"Synthetic","sha256":"abc123"},
             "npm":{"@playwright/mcp":"0.0.777"}}
            """);
        await writeAsync("upstream-snapshots/browsers.json", """
            {"browsers":[{"name":"chromium","revision":"4321","browserVersion":"999.0.0.0"},
                         {"name":"winldd","revision":"1007"}]}
            """);
        // ⚠️ 0.0.778 rather than the 0.0.777 above, deliberately. The manifest
        // reads this file's OWN judgedAgainst rather than the payload lock's
        // resolve, and two fixtures carrying one number could not tell the two
        // apart -- which is exactly the mistake a manifest exists to prevent.
        await writeAsync("tool-verdicts.json", """
            {"schemaVersion":1,
             "judgedAgainst":{"@playwright/mcp":"0.0.778","playwright-core":"1.99.0-alpha-2026-01-01"},
             "upstream":{"browser_close":{"verdict":"allow"}},
             "browserai":{"browserai_list":{"verdict":"answer"}}}
            """);

        return root;
    }

    private static async Task<string> WriteFeedAsync(string directory, params string[] versions)
    {
        _ = Directory.CreateDirectory(directory);

        var manifest = Path.Combine(directory, "releases.win.json");
        var assets = versions.Select(version =>
            $$"""{"PackageId":"BrowserAI","Version":"{{version}}","Type":"Full","FileName":"BrowserAI-{{version}}-full.nupkg","SHA1":"","SHA256":"","Size":1}""");

        await File.WriteAllTextAsync(manifest, $$"""{"Assets":[{{string.Join(",", assets)}}]}""");
        return manifest;
    }

    /// <summary>
    /// <b>Every release packs FULL packages only, and the feed it writes carries
    /// no <c>Delta</c> row.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's decision, 2026-09-22, verbatim:</b> <i>"always produce
    /// full packages only. The sizes are so small, and internet speeds nowadays
    /// are so fast that we don't want to exert any effort in creating deltas.
    /// Full downloads are always just easier."</i> It is a decision of record in
    /// [DECISIONS](../../DECISIONS.md#locking-logging-versioning-and-registration),
    /// not a property of how a particular cut happened to be run.
    /// </para>
    /// <para>
    /// <b>Two halves, because either alone is satisfiable without the other.</b>
    /// The script has to PASS the option, which a scan can see; and the option
    /// has to MEAN what the script assumes, which only <c>vpk</c> can say. Before
    /// 1.1.0 the feed carried no delta for a reason nobody chose — the clean
    /// re-pack empties <c>Releases/</c>, so there was never a previous package to
    /// delta against — and a test asserting "no delta row" without the option
    /// would have passed against a script that had never heard of it.
    /// </para>
    /// <para>
    /// <b>The mode is <c>None</c> and it was resolved from the tool rather than
    /// from memory.</b> <c>vpk pack --help</c> documents <c>--delta &lt;MODE&gt;</c>
    /// and does not enumerate the modes; handing it a value it cannot parse makes
    /// it name them: <i>"Cannot parse argument 'ZZZINVALID' for option '--delta'
    /// as expected type 'Velopack.Packaging.Compression.DeltaMode'. Did you mean
    /// one of the following? None"</i>. Read 2026-09-22 at <c>vpk</c> 1.2.158.
    /// </para>
    /// <para>
    /// <b>The pack is real and it is small.</b> 162 KB of a real PE rather than
    /// the 143 MB publish, because what is under test is <c>vpk</c>'s delta
    /// behaviour and not this product's bytes. Both arms run in about a second.
    /// It is gated on <see cref="SuiteEnvironment.RequirePackagedRelease"/>: a
    /// machine with no packed release has never run the release script and so
    /// has no <c>vpk</c> either, and under <c>BROWSERAI_RELEASE_RUN=1</c> that is
    /// a failure rather than a skip.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryReleasePacksFullPackagesOnlyAndTheFeedCarriesNoDeltaRow()
    {
        _ = SuiteEnvironment.RequirePackagedRelease();

        // ---- Half one: the script passes it, read out of the argument array
        // rather than the whole file, for the reason the sibling arm gives.
        var script = await File.ReadAllTextAsync(ReleaseScript);
        var start = script.IndexOf("$packArgs = @(", StringComparison.Ordinal);

        await Assert.That(start).IsGreaterThan(-1);

        var end = script.IndexOf("\n)", start, StringComparison.Ordinal);
        var passed = string.Join(
            '\n',
            script[start..end].Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

        await Assert.That(passed)
            .Contains("'--delta', 'None'")
            .Because("full packages only is a decision of record, and a release that stopped passing it would"
                + " start emitting deltas again with nothing to say so");

        // ---- Half two: the option means what the script assumes. A real pack,
        // twice, into a feed that already holds the previous full package.
        using var scratch = ScratchDirectory.Create("release-full-only");

        var packDir = Path.Combine(scratch.Path, "packdir");
        var withOption = Path.Combine(scratch.Path, "with-option");
        var without = Path.Combine(scratch.Path, "without");

        foreach (var directory in new[] { packDir, withOption, without })
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.Copy(SmallRealExecutable(), Path.Combine(packDir, "App.exe"));

        string[] common =
        [
            "pack",
            "--packId", ReleaseLayout.TestPackId,
            "--packDir", packDir,
            "--mainExe", "App.exe",
            "--channel", "win",
            "--legacyConsole",
        ];

        await Assert.That(await VpkAsync([.. common, "--packVersion", "1.0.0", "--outputDir", withOption])).IsEqualTo(0);

        // The same starting feed for both arms, so the only thing that differs
        // is the option.
        foreach (var file in new DirectoryInfo(withOption).EnumerateFiles())
        {
            file.CopyTo(Path.Combine(without, file.Name));
        }

        await Assert.That(await VpkAsync([.. common, "--packVersion", "1.1.0", "--outputDir", withOption, "--delta", "None"])).IsEqualTo(0);
        await Assert.That(await VpkAsync([.. common, "--packVersion", "1.1.0", "--outputDir", without])).IsEqualTo(0);

        // With the option: full packages only, and the feed says so.
        await Assert.That(DeltaPackagesIn(withOption)).IsEmpty();
        await Assert.That(await DeltaRowsInAsync(withOption)).IsEqualTo(0);
        await Assert.That(FullPackagesIn(withOption).Count).IsEqualTo(2);

        // ⚠️ THE POSITIVE CONTROL, and this arm is worth nothing without it. The
        // same two packs over the same feed WITHOUT the option do produce a
        // delta, so "no delta" above is the option working rather than vpk
        // having nothing to compare against.
        await Assert.That(DeltaPackagesIn(without)).IsNotEmpty();
        await Assert.That(await DeltaRowsInAsync(without)).IsEqualTo(1);
    }

    /// <summary>A real PE small enough to pack in about a second.</summary>
    /// <returns>Its path.</returns>
    private static string SmallRealExecutable()
    {
        var probe = new DirectoryInfo(Path.Combine(RepositoryLayout.Root.FullName, "tests", "BrowserAI.TestProbe", "bin"));

        return probe.Exists
            ? (probe.EnumerateFiles("BrowserAI.TestProbe.exe", SearchOption.AllDirectories).FirstOrDefault()?.FullName
                ?? throw new InvalidOperationException(
                    $"No BrowserAI.TestProbe.exe under '{probe.FullName}'. It is the small real PE this arm packs;"
                    + " build the solution and run again."))
            : throw new InvalidOperationException($"'{probe.FullName}' does not exist, so there is nothing small to pack.");
    }

    /// <param name="arguments">What to hand the packaging tool.</param>
    /// <returns>Its exit code.</returns>
    private static async Task<int> VpkAsync(string[] arguments)
    {
        var start = new ProcessStartInfo("vpk")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // House rule: no launch in this tree puts a window on the screen.
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var packaging = Process.Start(start)
            ?? throw new InvalidOperationException(
                "'vpk' did not start. The release script needs it too, and refuses without it:"
                + " dotnet tool install -g vpk --version <the Velopack the build resolved>.");

        var output = packaging.StandardOutput.ReadToEndAsync();
        var errors = packaging.StandardError.ReadToEndAsync();

        await packaging.WaitForExitAsync();

        _ = await output;
        _ = await errors;

        return packaging.ExitCode;
    }

    /// <param name="feed">A feed directory.</param>
    /// <returns>The delta packages in it.</returns>
    private static List<string> DeltaPackagesIn(string feed) =>
        [.. new DirectoryInfo(feed).EnumerateFiles("*-delta.nupkg").Select(file => file.Name)];

    /// <param name="feed">A feed directory.</param>
    /// <returns>The full packages in it.</returns>
    private static List<string> FullPackagesIn(string feed) =>
        [.. new DirectoryInfo(feed).EnumerateFiles("*-full.nupkg").Select(file => file.Name)];

    /// <param name="feed">A feed directory.</param>
    /// <returns>How many rows of the manifest say <c>Delta</c>.</returns>
    private static async Task<int> DeltaRowsInAsync(string feed)
    {
        var manifest = Path.Combine(feed, "releases.win.json");

        if (!File.Exists(manifest))
        {
            throw new InvalidOperationException($"'{manifest}' was not written, so the pack did not produce a feed at all.");
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifest));

        return document.RootElement.GetProperty("Assets").EnumerateArray()
            .Count(asset => string.Equals(asset.GetProperty("Type").GetString(), "Delta", StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs a release script and returns its exit code, its <b>verdict</b> and
    /// everything it said.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The verdict is stdout alone, and the two are separated because
    /// merging them asserted on the machine rather than on the script.</b> Every
    /// script here writes one word to stdout and its reasoning to stderr, so an
    /// exact-equality assertion over the concatenation is also an assertion that
    /// <c>pwsh</c> had nothing of its own to say. It does, on an ordinary
    /// machine: a drive letter whose root cannot be reached — a disconnected
    /// <c>net use</c> mapping is enough — makes <c>pwsh</c> write
    /// <i>"Attempting to perform the InitializeDefaultDrives operation on the
    /// 'FileSystem' provider failed"</i> to stderr at startup, and the assertion
    /// then fails for a reason no part of this repository owns. <b>Found
    /// 2026-08-19</b>, when `CanonicalPathTests` (then `SessionDirectoryGuardTests`) began creating a real
    /// mapped drive to prove the network refusal and this test went red beside
    /// it. <c>Everything</c> is still what the <c>Contains</c> arms read, because
    /// what those want is *did it explain itself*, and the explanation is on
    /// stderr.
    /// </remarks>
    /// <param name="script">The script to run.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The exit code, stdout alone, and stdout followed by stderr.</returns>
    private static async Task<(int Exit, string Verdict, string Everything)> RunAsync(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // Redirecting the two streams does NOT suppress the console.
            // Measured 2026-08-23: from a parent with no console of its own,
            // this launch without the flag put two visible windows on screen —
            // a Windows Terminal host and a pseudoconsole — and with it put
            // none. This one is the worst of the ten sites for it: `pwsh` is
            // started once per script arm.
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"'pwsh' did not start for {script}.");

        var captured = new StringBuilder();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var verdict = await stdout;
        _ = captured.Append(verdict).Append(await stderr);

        // Cached immediately: Process.ExitCode throws after Dispose(), and this
        // object is disposed on the way out of the using.
        return (process.ExitCode, verdict, captured.ToString());
    }
    /// <summary>
    /// <b>The icon that ships is the one the maintainer chose, in the shape
    /// Windows needs it in.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-09-16, when the icon stopped being a placeholder.</b>
    /// <c>assets\BrowserAI.ico</c> is candidate 3 of the ten drawn on 2026-09-15
    /// — a globe with a reading eye — chosen by the maintainer (Q196), and it is
    /// carried by both executables, the Setup stub, the Add/Remove entry and the
    /// Start Menu shortcut. One file, and nothing else changes with it.
    /// </para>
    /// <para>
    /// <b>What is asserted is the SHAPE, not the drawing.</b> A test that the
    /// 256 entry is a render of <c>icon.svg</c> would mean rasterising an SVG on
    /// every build — a browser, a renderer and a pixel comparison, to answer a
    /// question a person answers by looking. So the line that says <i>this is the
    /// right picture</i> is
    /// [the pre-cut check in RELEASING.md](../../RELEASING.md#7-build-clean),
    /// and what a run can hold is that the file is a four-entry icon with the
    /// sizes Windows asks for, 16, 32 and 48 as 32-bit DIBs and 256 as a
    /// PNG-compressed entry, and that the master raster beside it really is
    /// 256×256.
    /// </para>
    /// <para>
    /// ⚠️ <b>The planted red is a doctored file rather than the old
    /// placeholder</b>, and that is worth saying plainly. Candidate 1, which sat
    /// here until today, was packed by the same script and has the <b>same</b>
    /// directory shape — four entries, the same sizes, the same payload kinds —
    /// so swapping it back in would not move one assertion here. A check that
    /// cannot fail against the file it replaced is not evidence, so the controls
    /// below take the real bytes and break one property each: the entry count,
    /// the payload kind of the 256, the bit depth of the 16, the doubled height a
    /// mask entry declares, and the dimensions in a PNG's own header.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheShippedIconIsTheOneTheMaintainerChose()
    {
        var assets = Path.Combine(RepositoryLayout.Root.FullName, "assets");
        var ico = await File.ReadAllBytesAsync(Path.Combine(assets, "BrowserAI.ico"));

        await Assert.That(string.Join(Environment.NewLine, IconOffences(ico))).IsEmpty();

        // The three rasters the repository publishes, each at the size its name
        // claims. The social preview's is GitHub's own recommended size and the
        // reason it is pinned: that setting silently letterboxes anything else.
        await Assert.That(PngSize(await File.ReadAllBytesAsync(Path.Combine(assets, "icon-256.png")))).IsEqualTo((256, 256));
        await Assert.That(PngSize(await File.ReadAllBytesAsync(Path.Combine(assets, "icon-128.png")))).IsEqualTo((128, 128));
        await Assert.That(PngSize(await File.ReadAllBytesAsync(Path.Combine(assets, "social-preview.png")))).IsEqualTo((1280, 640));

        // ---- the doctored-file controls ------------------------------------
        // Each takes the real bytes and breaks exactly one property, so a check
        // that stopped reading would fail these rather than passing everything.
        await Assert.That(string.Join(" ", IconOffences(Doctored(ico, 4, 3)))).Contains("4 entries");
        await Assert.That(string.Join(" ", IconOffences(Doctored(ico, EntryOffset(ico, 3), 0x42)))).Contains("is not PNG-compressed");
        await Assert.That(string.Join(" ", IconOffences(Doctored(ico, 6 + 6, 8)))).Contains("bits per pixel");
        await Assert.That(string.Join(" ", IconOffences(Doctored(ico, EntryOffset(ico, 0) + 8, 16)))).Contains("doubled height");

        // And the raster half, which is one field in one header away from being
        // a check that reads nothing.
        var png = await File.ReadAllBytesAsync(Path.Combine(assets, "icon-256.png"));

        await Assert.That(PngSize(Doctored(png, 19, 0xFF))).IsNotEqualTo((256, 256));
    }

    /// <summary>The four bytes every PNG opens with.</summary>
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47];

    /// <summary>A copy of some bytes with one of them replaced.</summary>
    /// <param name="bytes">The original.</param>
    /// <param name="at">Which byte to replace.</param>
    /// <param name="value">What to put there.</param>
    /// <returns>The doctored copy.</returns>
    private static byte[] Doctored(byte[] bytes, int at, byte value)
    {
        var copy = bytes.ToArray();
        copy[at] = value;
        return copy;
    }

    /// <summary>Where one directory entry's payload begins.</summary>
    /// <param name="ico">The icon file.</param>
    /// <param name="index">The 0-based entry.</param>
    /// <returns>The offset.</returns>
    private static int EntryOffset(byte[] ico, int index) =>
        (int)BitConverter.ToUInt32(ico, 6 + (16 * index) + 12);

    /// <summary>
    /// Everything wrong with an icon file, as sentences.
    /// </summary>
    /// <remarks>
    /// <b>Read out of the bytes rather than through <c>System.Drawing</c>.</b>
    /// The loader answers <i>a 32×32 icon came back</i> for a file with any
    /// usable entry in it at all, which is exactly the question that does not
    /// need asking: what a shell, a task dialog and an Add/Remove list need is
    /// the specific sizes, and a file missing the 256 looks perfect to a loader
    /// and blurred on a 4K display.
    /// </remarks>
    /// <param name="ico">The file's bytes.</param>
    /// <returns>One line per defect.</returns>
    private static List<string> IconOffences(byte[] ico)
    {
        var offences = new List<string>();

        if (ico.Length < 6)
        {
            return ["the file is too short to carry an icon directory at all"];
        }

        if (BitConverter.ToUInt16(ico, 0) is not 0 || BitConverter.ToUInt16(ico, 2) is not 1)
        {
            offences.Add("the header is not 'reserved 0, type 1', so this is not an icon file");
        }

        var count = BitConverter.ToUInt16(ico, 4);

        // 16, 32 and 48 are what the shell asks for at every DPI a taskbar and
        // an Add/Remove list use; 256 is what Windows scales from above that.
        int[] expected = [16, 32, 48, 256];

        if (count != expected.Length)
        {
            offences.Add($"the directory declares {count} entries and this icon must carry {expected.Length} entries: {string.Join(", ", expected)}");
            return offences;
        }

        for (var index = 0; index < count; index++)
        {
            var at = 6 + (16 * index);
            var size = expected[index];

            // 0 in the width and height bytes IS 256 -- the field is one byte,
            // so 256 could not be written in it and the format spells it zero.
            var declared = ico[at] is 0 ? 256 : ico[at];
            var declaredHeight = ico[at + 1] is 0 ? 256 : ico[at + 1];

            if (declared != size || declaredHeight != size)
            {
                offences.Add($"entry {index} declares {declared}x{declaredHeight} where {size}x{size} is required");
                continue;
            }

            if (BitConverter.ToUInt16(ico, at + 4) is not 1 || BitConverter.ToUInt16(ico, at + 6) is not 32)
            {
                offences.Add($"entry {index} ({size}) declares {BitConverter.ToUInt16(ico, at + 6)} bits per pixel in {BitConverter.ToUInt16(ico, at + 4)} planes, and every entry here is 32-bit in one plane");
                continue;
            }

            var length = (int)BitConverter.ToUInt32(ico, at + 8);
            var offset = (int)BitConverter.ToUInt32(ico, at + 12);

            if (offset < 0 || length < 0 || offset + length > ico.Length)
            {
                offences.Add($"entry {index} ({size}) points at bytes {offset}..{offset + length} and the file is {ico.Length} bytes long");
                continue;
            }

            var payload = ico.AsSpan(offset, length);

            if (size is 256)
            {
                // The Vista+ PNG-compressed entry, and the reason the file is
                // 46 KB rather than 300: a 256 DIB is a quarter of a megabyte on
                // its own.
                if (!payload.StartsWith(PngSignature))
                {
                    offences.Add($"entry {index} (256) is not PNG-compressed, and a 256 written as a DIB is a quarter of a megabyte of icon");
                    continue;
                }

                var (width, height) = PngSize(payload.ToArray());

                if ((width, height) is not (256, 256))
                {
                    offences.Add($"entry {index}'s PNG says it is {width}x{height} and the directory says 256x256");
                }

                continue;
            }

            if (BitConverter.ToUInt32(ico, offset) is not 40)
            {
                offences.Add($"entry {index} ({size}) does not open with a 40-byte BITMAPINFOHEADER");
                continue;
            }

            if (BitConverter.ToUInt16(ico, offset + 14) is not 32)
            {
                offences.Add($"entry {index} ({size}) declares a bit depth of {BitConverter.ToUInt16(ico, offset + 14)} in its own header, and the directory says 32");
                continue;
            }

            // The doubled height is not a curiosity: it is how a DIB entry says
            // it carries an AND mask after the colour rows, and a header that
            // says `size` rather than `size * 2` makes Windows read the bottom
            // half of the image as the mask.
            if (BitConverter.ToInt32(ico, offset + 8) != size * 2)
            {
                offences.Add($"entry {index} ({size}) declares a height of {BitConverter.ToInt32(ico, offset + 8)} where a mask entry declares the doubled height {size * 2}");
            }
        }

        return offences;
    }

    /// <summary>
    /// What a PNG's own <c>IHDR</c> says its dimensions are.
    /// </summary>
    /// <remarks>
    /// Eight signature bytes, a four-byte length, the four-byte chunk type, then
    /// two big-endian dimensions. Read rather than decoded: nothing here needs a
    /// codec, and a reader that cannot be handed a corrupt image is a reader a
    /// control cannot be planted against.
    /// </remarks>
    /// <param name="png">The file's bytes.</param>
    /// <returns>The width and the height.</returns>
    private static (int Width, int Height) PngSize(byte[] png)
    {
        if (png.Length < 24 || !png.AsSpan(0, 4).SequenceEqual(PngSignature))
        {
            return (0, 0);
        }

        return (
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    /// <summary>
    /// A release publishes the installer, the full package and the feed, and the
    /// script says so in one place rather than a person choosing at upload time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Until 2026-09-23 nothing in this repository named an upload set.</b>
    /// <c>New-Release.ps1</c> does not upload — the publish is a hand-run
    /// <c>gh release create</c> at
    /// [RELEASING item 14](../../RELEASING.md#14-a-human-decides) — so the assets
    /// were whatever whoever ran it picked out of <c>Releases/</c>, and
    /// <c>v1.1.0</c> got seven assets because <c>v1.0.0</c> had seven. That is a
    /// judgement wearing the appearance of a procedure, and this arm is what
    /// makes it data.
    /// </para>
    /// <para>
    /// <b>Three names, each here for its own reason</b>: the installer is what a
    /// person runs, the full package is what every update and every rollback
    /// fetches, and <c>releases.&lt;channel&gt;.json</c> is the feed — the one file
    /// a Velopack client reads
    /// ([measured 2026-09-23](../../kb/packaging/velopack.md#nothing-anywhere-reads-releases-or-assetschanneljson-from-a-release--measured-2026-09-23)).
    /// </para>
    /// <para>
    /// <b>And four names are absent, each by a decision rather than by omission</b>
    /// — the portable zip and the manifest zip by the maintainer's answers to
    /// Q233, <c>RELEASES</c> and <c>assets.&lt;channel&gt;.json</c> by that
    /// measurement. They are asserted absent by name, because a set that merely
    /// happens not to contain something today is not a decision.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-23 with a doctored declaration</b>, run through the
    /// same reader as the real one rather than by editing <c>build/</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUploadSetIsDeclaredOnceAndIsTheInstallerTheFullPackageAndTheFeed()
    {
        var declared = ReleaseLayout.UploadSet;

        const string Version = "9.9.9";
        var resolved = declared.Select(name => Resolve(name, Version)).Order(StringComparer.Ordinal).ToArray();

        // Nothing is left unexpanded: a template naming a variable this suite
        // does not know would resolve to itself, read as a plausible name, and
        // slip past every comparison below.
        await Assert.That(string.Join(", ", resolved.Where(name => name.Contains('$', StringComparison.Ordinal)))).IsEmpty();

        // ⚠️ THE FOUR THAT ARE OUT, BY NAME, AND THIS COMES FIRST ON PURPOSE.
        // Each is a file this script really produces, and each is out for a
        // reason recorded beside the declaration -- so a set that grew one back
        // should fail saying WHICH, rather than saying it has four items.
        string[] notPublished =
        [
            $"{ReleaseLayout.DownloadId}{ReleaseLayout.DownloadSuffix}.zip",
            $"{ReleaseLayout.DownloadId}-{Version}-manifest.zip",
            "RELEASES",
            $"assets.{ReleaseLayout.Channel}.json",
        ];

        await Assert.That(string.Join(", ", notPublished.Intersect(resolved, StringComparer.Ordinal)))
            .IsEmpty()
            .Because("each of these left the upload set by a decision, and a release that published one again would be that decision reversed by accident");

        await Assert.That(resolved).IsEquivalentTo([
            $"{ReleaseLayout.DownloadId}{ReleaseLayout.DownloadSuffix}.exe",
            $"{ReleaseLayout.PackId}-{Version}-full.nupkg",
            $"releases.{ReleaseLayout.Channel}.json",
        ]);

        // And the templates as the script writes them. Asserting these as well
        // as what they resolve to is what keeps the declaration readable: a set
        // spelled out in literals would drift from the naming rules above it.
        await Assert.That(declared).IsEquivalentTo([
            "$downloadId$downloadSuffix.exe",
            "$packId-$PackVersion-full.nupkg",
            "releases.$Channel.json",
        ]);

        // And nothing belonging to the second pack or to the archive, which are
        // the two directories under Releases/ that are never published at all.
        await Assert.That(string.Join(", ", declared.Where(name =>
            name.Contains("test", StringComparison.OrdinalIgnoreCase)
            || name.Contains("archive", StringComparison.OrdinalIgnoreCase))))
            .IsEmpty();

        // ⚠️ THE POSITIVE CONTROL, through the real reader. A doctored
        // declaration must come back doctored, and a script that no longer
        // declares one at all must refuse rather than return nothing — an empty
        // set would satisfy every "is not published" assertion above.
        var doctored = ReleaseLayout.ReadUploadSet(
            "$packId = 'x'\n$uploadSet = @(\n    \"$downloadId$downloadSuffix.exe\"\n    \"$downloadId$downloadSuffix.zip\"\n)\n");

        await Assert.That(doctored).IsEquivalentTo(["$downloadId$downloadSuffix.exe", "$downloadId$downloadSuffix.zip"]);

        await Assert.That(() => ReleaseLayout.ReadUploadSet("$packId = 'x'\n"))
            .Throws<InvalidOperationException>();

        // The script also has to REFUSE a declared file that is not on disk, and
        // that refusal is a line rather than a behaviour this suite can run.
        var script = await File.ReadAllTextAsync(ReleaseScript);

        await Assert.That(script).Contains("The upload set names $name and $path does not exist");
        await Assert.That(script).Contains("Upload           = $uploadPaths");
    }

    /// <summary>
    /// The packer's own asset list is rewritten to the declared upload set, and
    /// what lands on disk is asserted rather than what was intended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Two mechanisms answered one question differently and never met.</b>
    /// <c>vpk pack</c> writes <c>assets.&lt;channel&gt;.json</c> naming everything
    /// it produced, and <c>vpk upload github</c> uploads <b>every file listed in
    /// it</b> -- <c>BuildAssets.Read</c> then <c>build.GetFilePaths()</c>, read at
    /// Velopack 1.2.158. So the portable archive would have been published by the
    /// one command nobody here runs, contradicting
    /// <see cref="TheUploadSetIsDeclaredOnceAndIsTheInstallerTheFullPackageAndTheFeed"/>
    /// and [RELEASING](../../RELEASING.md#what-a-release-publishes). Q235 b is the
    /// maintainer's answer: make the list agree with the declaration.
    /// </para>
    /// <para>
    /// <b>Driven rather than scanned, which is why the step is its own script.</b>
    /// A rewrite asserted by reading <c>New-Release.ps1</c> for a line proves the
    /// line was typed, never that anything was rewritten --
    /// <c>Clear-TestPackFeed.ps1</c>'s reason, one step later.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-23 with the filter absent</b>: the step wrote the
    /// packer's list back unchanged and this arm named `BrowserAI.zip` as still
    /// listed, which is the defect the step exists for.
    /// </para>
    /// <para>
    /// ⚠️ <b>One thing it cannot cover and does not claim to.</b> On the default
    /// Windows channel <c>vpk upload github</c> also uploads a legacy
    /// <c>RELEASES</c> of its own accord, from no list at all. Nothing in this
    /// file can stop that; what stops it is that releases here are published with
    /// <c>gh release create</c> from the declared set.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePackersOwnAssetListIsRewrittenToTheDeclaredUploadSet()
    {
        using var scratch = ScratchDirectory.Create("release-upload-assets");

        const string Version = "9.9.9";
        var installer = $"{ReleaseLayout.DownloadId}{ReleaseLayout.DownloadSuffix}.exe";
        var portable = $"{ReleaseLayout.DownloadId}{ReleaseLayout.DownloadSuffix}.zip";
        var package = $"{ReleaseLayout.PackId}-{Version}-full.nupkg";
        var feed = $"releases.{ReleaseLayout.Channel}.json";
        var declared = ReleaseLayout.UploadSet.Select(name => Resolve(name, Version)).ToArray();

        // The shape vpk really writes, taken from the file this repository's own
        // pack produced: three entries, the portable archive in the middle.
        var asPacked = $$"""
            [{"RelativeFileName":"{{installer}}","Type":"Installer"},{"RelativeFileName":"{{portable}}","Type":"Portable"},{"RelativeFileName":"{{package}}","Type":"Full"}]
            """;

        var path = Path.Combine(scratch.Path, feed.Replace("releases.", "assets.", StringComparison.Ordinal));
        await File.WriteAllTextAsync(path, asPacked);

        var (exit, _, output) = await RunAsync(UploadAssetsScript, "-Path", path, "-Keep", string.Join(",", declared));

        await Assert.That(exit).IsEqualTo(0);

        var written = Names(await File.ReadAllTextAsync(path));

        // The portable archive first, and by name: a list that kept it should
        // fail saying WHICH file it kept rather than that two sets differ.
        await Assert.That(written)
            .DoesNotContain(portable)
            .Because("vpk upload publishes every file this list names, and the portable archive left the upload set by the maintainer's decision");

        await Assert.That(written).IsEquivalentTo([installer, package]);

        // It says what it did, because a silent rewrite in a release script is
        // indistinguishable from a step that did not run.
        await Assert.That(output).Contains(portable);

        // And it is idempotent: the second cut of a release must not depend on
        // whether somebody ran this by hand after the first.
        var (againExit, _, _) = await RunAsync(UploadAssetsScript, "-Path", path, "-Keep", string.Join(",", declared));

        await Assert.That(againExit).IsEqualTo(0);
        await Assert.That(Names(await File.ReadAllTextAsync(path))).IsEquivalentTo([installer, package]);

        // Written the way everything else here is written.
        var bytes = await File.ReadAllBytesAsync(path);

        await Assert.That(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).IsFalse();
        await Assert.That(Encoding.UTF8.GetString(bytes)).DoesNotContain("\r\n");

        // ---- The control, in the other direction ----------------------------
        // Keeping the portable archive must LEAVE it, or the arm above is
        // asserting a step that removes everything it was not asked about.
        var control = Path.Combine(scratch.Path, "assets.control.json");
        await File.WriteAllTextAsync(control, asPacked);

        var (controlExit, _, _) = await RunAsync(
            UploadAssetsScript, "-Path", control, "-Keep", string.Join(",", [installer, portable, package]));

        await Assert.That(controlExit).IsEqualTo(0);
        await Assert.That(Names(await File.ReadAllTextAsync(control))).IsEquivalentTo([installer, portable, package]);

        // ---- The two refusals -----------------------------------------------
        // A list with no Full entry is a feed with no rows: vpk builds
        // releases.<channel>.json out of exactly those.
        var noPackage = Path.Combine(scratch.Path, "assets.nopackage.json");
        await File.WriteAllTextAsync(noPackage, asPacked);

        var (noPackageExit, _, noPackageSays) = await RunAsync(
            UploadAssetsScript, "-Path", noPackage, "-Keep", installer);

        await Assert.That(noPackageExit).IsNotEqualTo(0);
        await Assert.That(noPackageSays).Contains("Full");

        // And a list that is not there at all is a refusal rather than a no-op,
        // because an upload set nothing enforces is the state this step ends.
        var (absentExit, _, absentSays) = await RunAsync(
            UploadAssetsScript, "-Path", Path.Combine(scratch.Path, "never.json"), "-Keep", installer);

        await Assert.That(absentExit).IsNotEqualTo(0);
        await Assert.That(absentSays).Contains("no asset list");

        // ---- And the release script really calls it -------------------------
        var script = await File.ReadAllTextAsync(ReleaseScript);

        await Assert.That(script).Contains("Set-UploadAssets.ps1");
        await Assert.That(script).Contains("-Keep $uploadSet");
    }

    /// <summary>The file names one <c>assets.&lt;channel&gt;.json</c> holds.</summary>
    /// <param name="json">The file's text.</param>
    /// <returns>Each entry's <c>RelativeFileName</c>, in the order written.</returns>
    private static List<string> Names(string json) =>
    [
        .. JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Select(entry => entry.GetProperty("RelativeFileName").GetString()!),
    ];

    /// <summary>
    /// Every top-level file the release directory holds is either published or
    /// deliberately not, and nothing is unclassified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the half that catches an artifact nobody has decided about.</b>
    /// The arm above holds that the declaration says what it should; this one
    /// holds that the declaration still covers everything a pack leaves behind,
    /// so a new output — a signature, a checksum file, a second channel — is a red
    /// build until somebody says whether it is published.
    /// </para>
    /// <para>
    /// <b>It needs a machine that has packed</b>, which is what the capability is
    /// for. <c>archive/</c> and <c>test-pack/</c> are directories and are out of
    /// scope by construction: neither is ever published, and the second is the
    /// suite's own installer.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NothingElseInTheReleaseDirectoryIsPublished()
    {
        _ = SuiteEnvironment.RequirePackagedRelease();

        var directory = new DirectoryInfo(ReleaseLayout.Directory);
        var present = directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly).Select(file => file.Name).ToArray();

        await Assert.That(present.Length).IsGreaterThan(2);

        await Assert.That(string.Join(Environment.NewLine, Unclassified(present)))
            .IsEmpty()
            .Because("a file in the release directory that is neither in the upload set nor in the list of things deliberately kept out of it is one nobody has decided about");

        // ⚠️ THE POSITIVE CONTROL, over the same classifier. A stranger must be
        // named, and the files that are deliberately kept out must not be.
        await Assert.That(Unclassified(["a-signature-nobody-decided-about.sig"]).Count).IsEqualTo(1);
        await Assert.That(Unclassified(["a-signature-nobody-decided-about.sig"])[0]).Contains(".sig");
        await Assert.That(Unclassified(["RELEASES", $"assets.{ReleaseLayout.Channel}.json"])).IsEmpty();
    }

    /// <summary>
    /// Expands one declared upload-set name into the file it names.
    /// </summary>
    /// <param name="name">The template as the script writes it.</param>
    /// <param name="version">The version to resolve <c>$PackVersion</c> with.</param>
    /// <returns>The file name.</returns>
    private static string Resolve(string name, string version) => name
        .Replace("$downloadId", ReleaseLayout.DownloadId, StringComparison.Ordinal)
        .Replace("$downloadSuffix", ReleaseLayout.DownloadSuffix, StringComparison.Ordinal)
        .Replace("$packId", ReleaseLayout.PackId, StringComparison.Ordinal)
        .Replace("$PackVersion", version, StringComparison.Ordinal)
        .Replace("$Channel", ReleaseLayout.Channel, StringComparison.Ordinal);

    /// <summary>
    /// The names that are neither published nor recorded as deliberately kept
    /// out of the release.
    /// </summary>
    /// <remarks>
    /// A pure function over names so the control can be planted without writing
    /// a stranger into <c>Releases/</c>, which is a real directory on the
    /// maintainer's machine holding a real release.
    /// </remarks>
    /// <param name="names">The file names to classify.</param>
    /// <returns>One line per name nobody has decided about.</returns>
    private static List<string> Unclassified(IEnumerable<string> names)
    {
        var published = ReleaseLayout.UploadSet.Select(name => Resolve(name, "*")).ToArray();

        return
        [
            .. names
                .Where(name => !Matches(name, published) && !Matches(name, KeptOutOfTheRelease))
                .Select(name => $"{name}: in {ReleaseLayout.Directory} and classified neither way"),
        ];
    }

    /// <summary>
    /// What a pack leaves in the release directory that is deliberately not
    /// published, each with the decision that put it here.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>the portable archive — Q233, <i>"2 drop and update the readme to not mention it"</i>;</item>
    /// <item>the resolved-set manifest — Q233, <i>"7 move it"</i>; it is committed under <c>docs/evidence/</c> per release instead;</item>
    /// <item><c>RELEASES</c> — a Squirrel-migration shim nothing here has a predecessor for, and nothing reads it from a release;</item>
    /// <item><c>assets.&lt;channel&gt;.json</c> — the local pack-to-upload hand-off <c>vpk upload</c> reads from this directory, and which nobody fetches from a release page;</item>
    /// <item>a delta package, which no release has carried since the full-packages-only decision and which a feed would have to name before anybody could fetch it.</item>
    /// </list>
    /// </remarks>
    private static string[] KeptOutOfTheRelease { get; } =
    [
        $"{ReleaseLayout.DownloadId}{ReleaseLayout.DownloadSuffix}.zip",
        $"{ReleaseLayout.DownloadId}-*-manifest.zip",
        "RELEASES",
        $"assets.{ReleaseLayout.Channel}.json",
        $"{ReleaseLayout.PackId}-*-delta.nupkg",
    ];

    /// <summary>Whether a name matches one of a set of patterns whose only wildcard is <c>*</c>.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="patterns">The patterns.</param>
    /// <returns>Whether any matches.</returns>
    private static bool Matches(string name, IEnumerable<string> patterns) =>
        patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true));
}

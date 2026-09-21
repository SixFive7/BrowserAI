// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using System.Text.RegularExpressions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Asserts the shape of the vendored payload's declared dependencies.
/// </summary>
/// <remarks>
/// <para>
/// The npm half of the float. <c>Directory.Packages.props</c> is guarded by
/// <see cref="BuildConfigurationTests"/>; this guards the one other place a
/// version can be declared, and the failure it prevents is the same one:
/// a payload that stopped following upstream while every surface signal — a
/// green build, a committed lock, a passing suite — reads healthy.
/// </para>
/// <para>
/// The two declaration tests read the tracked files under
/// <c>build/payload/</c>, so they run on a clean clone with no payload built.
/// The two content tests read the assembled tree under <c>payload/</c> and go
/// through <see cref="SuiteEnvironment"/>'s gate for it, because a scan of a
/// tree that is not there passes trivially and reads identically to a scan that
/// found nothing — which is the whole failure this suite's capability gate
/// exists to make loud. <i>(Corrected 2026-08-17, previously "These read the
/// tracked files under <c>build/payload/</c>, never the assembled tree under
/// <c>payload/</c>".)</i>
/// </para>
/// </remarks>
internal sealed partial class PayloadTests
{
    private static readonly string[] ExpectedDependencies = ["@playwright/mcp"];

    /// <summary>
    /// The sentence in <c>DECISIONS.md</c> that introduces the chain as it ships
    /// today, as distinct from the 0.0.79-era one printed above it.
    /// </summary>
    private const string WorkedExampleAnchor = "What the payload ships today, read from";

    /// <summary>A Markdown code fence, named rather than spelled at each use.</summary>
    private const string Fence = "```";

    /// <summary>
    /// What a platform-native binary looks like on any platform, not only this
    /// one — a tree that is portable is portable everywhere or it is not
    /// portable.
    /// </summary>
    private static readonly HashSet<string> NativeExtensions =
        new([".node", ".dll", ".exe", ".so", ".dylib", ".a", ".lib", ".pdb"], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] TheOnePortableBinary =
        ["node_modules/playwright-core/lib/webp_codec.wasm"];

    [Test]
    public async Task ThePayloadDeclaresOneDependencyAndItIsADistTag()
    {
        using var manifest = ReadJson("package.json");

        var dependencies = manifest.RootElement.GetProperty("dependencies")
            .EnumerateObject()
            .Select(property => (property.Name, Value: property.Value.GetString()))
            .ToList();

        // playwright-core is the one that matters. It arrives as
        // @playwright/mcp's own exact dependency, and upstream publishes daily
        // alphas alongside a lagging `latest`: on 2026-08-15 npm `latest` was
        // 1.62.1 while the shipping version was 1.63.0-alpha-2026-08-05.
        // Declaring it here would resolve the wrong one, and the tree would
        // still install.
        await Assert.That(dependencies.Select(dependency => dependency.Name))
            .IsEquivalentTo(ExpectedDependencies);

        // `latest` is the dist-tag, so the payload build re-resolves on every
        // run. A range — `^0.0.79` — would look equally floating and would pin
        // the major forever, which for a 0.0.x package pins everything.
        await Assert.That(dependencies[0].Value).IsEqualTo("latest");
    }

    [Test]
    public async Task TheLockRecordsUpstreamsOwnExactPinOfPlaywrightCore()
    {
        var declared = DeclaredPlaywrightCore();

        // An exact version, byte for byte, not a range that happens to match.
        // If upstream ever loosened this pin the payload would start floating
        // on a second axis, and the lock alone would not say so.
        //
        // ⚠️ Corrected 2026-09-21 (previously "This arm used to also assert
        // `declared == resolved`, and that half moved to
        // `TheResolvedPlaywrightCoreIsWhateverTheOverrideSays` on 2026-09-17 when
        // the dated override arrived"). The override was retired on 2026-09-21
        // when @playwright/mcp 0.0.82 shipped a playwright-core carrying the fix
        // it was taken for, so the arm it moved to is gone and the half comes
        // back here. THIS IS ALSO WHAT WOULD CATCH AN OVERRIDE BEING ADDED AGAIN:
        // an `overrides` entry makes the lock resolve to something other than
        // upstream's declared pin, and this line goes red -- which is the same
        // check `build/Build-Payload.ps1` makes against the live resolution.
        await Assert.That(declared).DoesNotContain("^");
        await Assert.That(declared).DoesNotContain("~");
        await Assert.That(declared).DoesNotContain("*");
        await Assert.That(declared).DoesNotContain(" ");

        var resolved = ReadJson("package-lock.json").RootElement
            .GetProperty("packages")
            .GetProperty("node_modules/playwright-core")
            .GetProperty("version")
            .GetString();

        await Assert.That(resolved)
            .IsEqualTo(declared)
            .Because(
                "playwright-core ships as @playwright/mcp's own exact dependency and nothing else resolves it. "
                + "A resolved version that is not the declared one means something in build/payload/package.json "
                + "is holding it somewhere else -- which between 2026-09-17 and 2026-09-21 was a dated exception "
                + "with a written exit, and is now a change to the versioning policy rather than a build detail. "
                + "See DECISIONS.md, 'The two exceptions to the versioning policy'.");
    }

    /// <summary>
    /// <b><c>DECISIONS.md</c> section 2's worked example states the chain the
    /// committed records state.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// That section prints the payload's chain — wrapper, core, browser revision
    /// — as a worked example, and a worked example is the shape of claim that
    /// goes stale in silence: every version in it was true on the day it was
    /// written, nothing re-reads it, and a reader meets a self-consistent
    /// picture of a payload nobody ships. It stood at <c>@playwright/mcp</c>
    /// 0.0.79 / <c>playwright-core</c> 1.63.0-alpha-2026-08-05 / chromium 1237
    /// until 2026-09-21, and the sentence calling <c>chromium-1237</c> <i>the one
    /// at the end of our chain</i> had been false for weeks.
    /// </para>
    /// <para>
    /// <b>Every number comes from a committed record</b>, so this runs on a clean
    /// clone with no payload assembled: the two package versions out of
    /// <c>build/payload/package-lock.json</c>, which
    /// <see cref="TheAssembledManifestDeclaresWhatTheCommittedLockRecords"/>
    /// holds to the assembled tree, and the revision and browser version out of
    /// <c>upstream-snapshots/browsers.json</c> through
    /// <see cref="BrowserAiPaths.RevisionOf"/> and
    /// <see cref="BrowserAiPaths.BrowserVersionOf"/>, which is the suite's one
    /// reader of that snapshot. Both files are regenerated from the resolved
    /// payload, which is what makes this an answer about today rather than about
    /// whenever the prose was written.
    /// </para>
    /// <para>
    /// <b>The sentence is the anchor and rewording it fails the build.</b> That
    /// is <c>RecordedCountTests</c>' trade taken deliberately: a check keyed on
    /// prose can be unhooked by editing the prose, so the unhooking is made loud
    /// rather than silent. <b>The 0.0.79 chain printed above it is deliberately
    /// NOT read</b> — it is a true record of one day, and holding a record to
    /// today's manifest would demand it be rewritten at every roll, which is the
    /// same exemption <c>ThirdPartyNoticeTests</c> gives a correction stamp's
    /// <c>previously</c> span, for the same reason.
    /// </para>
    /// <para>
    /// <i>Corrected 2026-09-21 (previously "The wrapper's own declared pin is
    /// read <b>only while an override is in force</b>, because it is the
    /// override that gives the example a fourth number to print").</i> The
    /// exception was retired that day, so the chain has three links again and
    /// nothing here reads a fourth.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheWorkedExampleStatesTheChainTheCommittedRecordsState()
    {
        var example = WorkedExample();
        var disagreements = new List<string>();

        string wrapper;
        string resolved;

        using (var lockFile = ReadJson("package-lock.json"))
        {
            var packages = lockFile.RootElement.GetProperty("packages");

            wrapper = packages.GetProperty("node_modules/@playwright/mcp").GetProperty("version").GetString()!;
            resolved = packages.GetProperty("node_modules/playwright-core").GetProperty("version").GetString()!;
        }

        Disagrees(disagreements, example, WrapperInExample(), "@playwright/mcp", wrapper, "the payload lock");
        Disagrees(disagreements, example, ResolvedCoreInExample(), "playwright-core", resolved, "the payload lock");

        Disagrees(
            disagreements,
            example,
            ChromiumRevisionInExample(),
            "the chromium revision",
            BrowserAiPaths.RevisionOf("chromium"),
            "the committed browsers.json snapshot");

        Disagrees(
            disagreements,
            example,
            ChromiumBrowserVersionInExample(),
            "the chromium browser version",
            BrowserAiPaths.BrowserVersionOf("chromium"),
            "the committed browsers.json snapshot");

        await Assert.That(disagreements).IsEmpty();
    }

    [Test]
    public async Task TheAssembledManifestDeclaresWhatTheCommittedLockRecords()
    {
        // The payload's own node_modules manifest is what the running child was
        // built from; the committed lock is what the exit above reads. A payload
        // assembled from something else would leave the exit reading a record of
        // a tree nobody is running.
        SuiteEnvironment.RequireRepositoryPayload();

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryPayload.Layout.Root, "mcp", "node_modules", "@playwright", "mcp", "package.json")));

        await Assert.That(manifest.RootElement.GetProperty("dependencies").GetProperty("playwright-core").GetString())
            .IsEqualTo(DeclaredPlaywrightCore());

        using var installed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryPayload.Layout.Root, "mcp", "node_modules", "playwright-core", "package.json")));

        await Assert.That(installed.RootElement.GetProperty("version").GetString())
            .IsEqualTo(DeclaredPlaywrightCore());
    }

    [Test]
    public async Task TheVendoredTreeCarriesNoPlatformNativeBinary()
    {
        // The one claim in the payload table that nothing checked. "Zero native
        // binaries; the tree is portable JS" is what makes the JS half of the
        // payload a per-file delta of text rather than an architecture-specific
        // artifact -- and it is upstream's property, not ours, so it can be
        // undone by a dependency upstream adds without a word to us. A `.node`
        // arriving in `mcp\` would also cross the batteries-included boundary:
        // it would need a toolchain the installer does not carry.
        //
        // `node\` is deliberately outside the scan. `node.exe` is the native
        // binary the payload exists to ship.
        SuiteEnvironment.RequireRepositoryPayload();

        var mcp = new DirectoryInfo(Path.Combine(RepositoryPayload.Layout.Root, "mcp"));

        var native = mcp.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => NativeExtensions.Contains(file.Extension))
            .Select(file => Path.GetRelativePath(mcp.FullName, file.FullName))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(string.Join(", ", native)).IsEmpty();
    }

    [Test]
    public async Task TheOneNonJavaScriptArtefactInTheTreeIsPortableAndIsNamed()
    {
        // Measured 2026-08-17 @ @playwright/mcp 0.0.79 / playwright-core
        // 1.63.0-alpha-2026-08-05: the tree is not JS alone. It carries exactly
        // one `.wasm`, and a `.wasm` is portable bytecode rather than a native
        // binary -- so the premise survives and the wording above it did not.
        // Named rather than allowed by extension, because the interesting event
        // is a *second* one arriving: upstream adding a WASM codec is how a
        // "portable JS tree" acquires a component nobody reviewed.
        SuiteEnvironment.RequireRepositoryPayload();

        var mcp = new DirectoryInfo(Path.Combine(RepositoryPayload.Layout.Root, "mcp"));

        var portable = mcp.EnumerateFiles("*.wasm", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(mcp.FullName, file.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(portable).IsEquivalentTo(TheOnePortableBinary);
    }

    [Test]
    public async Task TheAssembledPayloadSatisfiesTheFourChecksThatLivedOnlyInTheBuildScript()
    {
        // Build-order step 3's done-tests. Every one of them was run once, by
        // hand, on the day the payload was first assembled, and then lived in a
        // PowerShell script the suite never invokes -- so a payload rebuilt
        // wrongly on any later day is caught by nothing. They cost milliseconds
        // apiece against a tree that is already on disk, which is why "the
        // script asserts it" was never a good enough answer.
        SuiteEnvironment.RequireRepositoryPayload();

        var layout = RepositoryPayload.Layout;
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(layout.Root, "payload.json")));

        // 1 -- node.exe is the version the resolver returned, asked of the
        // binary rather than read back out of the manifest beside it. A
        // manifest agreeing with itself proves nothing about the executable.
        var recorded = manifest.RootElement.GetProperty("node").GetProperty("version").GetString();
        var (versionExit, versionOutput) = await RunAsync(layout.NodeExecutable, "--version");

        await Assert.That(versionExit).IsEqualTo(0);
        await Assert.That(versionOutput.Trim()).IsEqualTo(recorded);

        // 2 -- the vendored cli.js runs under that node and prints a usage
        // block. This is the check that would have caught a tree that installed
        // and cannot start, which is the founding failure class of this project
        // arriving from npm instead of from a browser.
        var (helpExit, helpOutput) = await RunAsync(layout.NodeExecutable, $"\"{layout.PlaywrightMcpCli}\" --help");

        await Assert.That(helpExit).IsEqualTo(0);
        await Assert.That(helpOutput).Contains("Usage:");

        // 3 -- `.links/` absent, `LICENSE` present. The first is vacuous today
        // and is asserted anyway, because it stops being vacuous the moment
        // anyone runs an installer with PLAYWRIGHT_BROWSERS_PATH pointed at the
        // staging tree -- the exact conflation §A carries a correction for. The
        // second is the obligation: Node's LICENSE ships or the payload is not
        // redistributable, and it comes out of the archive rather than from a
        // standalone URL that does not exist.
        await Assert.That(Directory.Exists(Path.Combine(layout.Root, "mcp", ".links"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(layout.Root, "node", ".links"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(layout.Root, "node", "LICENSE"))).IsTrue();
    }

    [Test]
    public async Task TheBrowsersRootHoldsFullChromiumAndNoHeadlessShell()
    {
        // Step 3's fourth done-test, and the one with a decision inside it.
        // `--no-shell` is load-bearing rather than tidy: full Chromium in every
        // mode is settled, and a shell directory appearing here means something
        // asked for one -- 268.49 MB nobody chose, on every machine.
        //
        // The path also pins the asymmetry §A names: the outer directory uses
        // underscores and the inner one dashes, so a path built on the wrong
        // guess fails here rather than at a browser launch.
        SuiteEnvironment.RequireProvisionedChromium();

        await Assert.That(File.Exists(BrowserAiPaths.ExpectedChromiumExecutable)).IsTrue();
        await Assert.That(BrowserAiPaths.ExpectedChromiumExecutable).Contains("chrome-win64");
        await Assert.That(BrowserAiPaths.ChromiumDirectory).Contains("chromium-");
        await Assert.That(Directory.Exists(BrowserAiPaths.HeadlessShellDirectory)).IsFalse();
    }

    [Test]
    public async Task UpstreamStillSerialisesEveryInstallOnOneLockOverTheWholeBrowsersRoot()
    {
        // ⚠️ THIS IS THE MECHANISM A CORRECTED COMMENT NOW RESTS ON, and it is
        // upstream's property rather than ours, so a `playwright-core` bump can
        // take it away without a word.
        //
        // BrowserAI's provisioning mutex is keyed on (browsers root, FAMILY)
        // -- `BrowserProvisioner.MutexNameFor` -- so a chromium install and a
        // firefox install run concurrently by design, and BOTH lay down
        // `ffmpeg` and `winldd` in the one browsers root. Two extractions into
        // one tree is the corruption the provisioning mutex exists to prevent,
        // and nothing on OUR side prevents this particular pair.
        //
        // Upstream does. `registry.install()` takes a proper-lockfile directory
        // lock at `<PLAYWRIGHT_BROWSERS_PATH>\__dirlock` before it touches any
        // executable and holds it for the whole install, so every install on
        // this machine against this root serialises. Measured 2026-08-19 three
        // ways -- kb/playwright/provisioning-and-timings.md -- and the
        // measurement is the evidence; this test is what notices if the
        // mechanism is removed.
        //
        // Read out of the ASSEMBLED payload, which is the code that actually
        // runs, rather than out of a package the build might resolve
        // differently. Source order rather than execution order is what an
        // assertion over text can establish, and for this straight-line
        // function they are the same thing.
        SuiteEnvironment.RequireRepositoryPayload();

        var bundle = await File.ReadAllTextAsync(Path.Combine(
            RepositoryPayload.Layout.Root,
            "mcp",
            "node_modules",
            "playwright-core",
            "lib",
            "coreBundle.js"));

        // Each of the four is unique in the bundle, so an index is an identity
        // and not a first match. A rename upstream fails here loudly, which is
        // the point: the alternative is this scan finding nothing and saying so
        // by passing.
        var install = bundle.IndexOf("async install(executablesToInstall", StringComparison.Ordinal);
        var lockfileName = bundle.IndexOf("\"__dirlock\"", StringComparison.Ordinal);
        var acquire = bundle.IndexOf("await lock(registryDirectory", StringComparison.Ordinal);
        var perExecutable = bundle.IndexOf("executable._install(", StringComparison.Ordinal);
        var release = bundle.IndexOf("releaseLock()", StringComparison.Ordinal);

        await Assert.That(install).IsGreaterThanOrEqualTo(0);
        await Assert.That(lockfileName).IsGreaterThanOrEqualTo(0);
        await Assert.That(acquire).IsGreaterThanOrEqualTo(0);
        await Assert.That(perExecutable).IsGreaterThanOrEqualTo(0);
        await Assert.That(release).IsGreaterThanOrEqualTo(0);

        // The lock is named and taken inside `install`, before the loop that
        // installs each executable, and released after it. That ordering is the
        // whole guarantee: a lock taken per executable, or after the first
        // download, would leave exactly the window this closes.
        await Assert.That(lockfileName).IsGreaterThan(install);
        await Assert.That(acquire).IsGreaterThan(lockfileName);
        await Assert.That(perExecutable).IsGreaterThan(acquire);
        await Assert.That(release).IsGreaterThan(perExecutable);

        // And the lock covers the root BOTH families are pointed at. Without
        // this, a per-package lock directory would satisfy everything above and
        // serialise nothing that matters to us.
        await Assert.That(bundle).Contains("registryDirectory = ", StringComparison.Ordinal);
        await Assert.That(bundle).Contains("getFromENV(\"PLAYWRIGHT_BROWSERS_PATH\")", StringComparison.Ordinal);
    }

    /// <summary>
    /// A payload that re-resolved makes the published binary stale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It did not, and the gap fails in the direction nothing reports.</b>
    /// A publish copies the resolved payload beside the executable, so a
    /// re-resolve that moved <c>@playwright/mcp</c>, <c>playwright-core</c> or
    /// <c>node</c> left every slice arm driving a published tree carrying the
    /// old one — reading as fresh, with the lock in the tree saying otherwise.
    /// Found 2026-08-29 during release preparation and benign on the day, only
    /// because that re-resolve had come back byte for byte.
    /// </para>
    /// <para>
    /// <b>Asserted over the corpus rather than over the check</b>, for the same
    /// reason as
    /// <see cref="SqliteTests.TheFreshnessCheckWatchesTheVendoredAmalgamation"/>:
    /// a staleness check is silent by construction about what it never looked
    /// at, and passing is exactly what it does when a file is outside it.
    /// </para>
    /// <para>
    /// <b>And over the corpus rather than over the tree, because the tree is
    /// pruned.</b> <see cref="RepositoryLayout"/> drops any directory named
    /// <c>payload</c> during the walk, so no corpus it produces can contain this
    /// file however the enumeration is widened — which is why the second arm
    /// below asserts the exact relative path rather than a pattern.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheFreshnessCheckWatchesThePayloadsProvenanceStamp()
    {
        var watched = PublishedSlice.FreshnessInputs
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName).Replace('\\', '/'))
            .ToList();

        // The positive control: the corpus is the one that was already there, so
        // an empty or broken enumeration cannot make the arm below pass.
        await Assert.That(watched).Contains("src/BrowserAI/Program.cs");
        await Assert.That(watched).Contains("third-party/sqlite/sqlite3.c");

        await Assert.That(watched)
            .Contains("build/payload/package-lock.json")
            .Because("a publish copies the resolved payload beside the executable, so a re-resolve that moved @playwright/mcp, playwright-core or node must make the publish stale -- and the lock is the committed record of exactly that resolution, while the resolved tree itself is gitignored and pruned from every walk");

        // The file has to BE there for watching it to mean anything: a FileInfo
        // for a path that does not exist has LastWriteTimeUtc of 1601 and would
        // sit in the corpus for ever, never newer than anything, watching
        // nothing. This is the arm that separates watched from named.
        await Assert.That(PublishedSlice.PayloadProvenanceStamp.Exists)
            .IsTrue()
            .Because("a missing file never reads as newer than the binary, so a freshness input that is not there is indistinguishable from one that never fires");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string executable, string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // Redirecting the three streams does NOT suppress the console.
            // Measured 2026-08-23: from a parent with no console of its own,
            // this launch without the flag put two visible windows on screen
            // and with it put none. A suite run from a terminal hides the
            // difference, because the child joins the terminal's console
            // instead of allocating one.
            CreateNoWindow = true,

            // Explicit, because an unset WorkingDirectory passes null to
            // CreateProcess and the child inherits the test host's -- which for
            // a tree that resolves anything relative is a different answer per
            // runner.
            WorkingDirectory = RepositoryLayout.Root.FullName,
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, output + error);
    }

    /// <summary><c>@playwright/mcp</c>'s own declared <c>playwright-core</c> dependency.</summary>
    /// <returns>The exact version upstream pins, as the committed lock records it.</returns>
    private static string? DeclaredPlaywrightCore()
    {
        using var lockFile = ReadJson("package-lock.json");

        return lockFile.RootElement
            .GetProperty("packages")
            .GetProperty("node_modules/@playwright/mcp")
            .GetProperty("dependencies")
            .GetProperty("playwright-core")
            .GetString();
    }

    // ⚠️ RETIRED 2026-09-21, and named here so the deletion is a record rather
    // than an absence. Three members stood between these two comments --
    // `OverriddenPlaywrightCore`, which read `overrides.playwright-core` out of
    // build/payload/package.json; `TheOverrideHasExpired`, which compared the
    // wrapper's declared pin against it; and `Orderable`, which turned a
    // playwright-core version into a sortable string and THREW on any shape
    // other than `<major>.<minor>.<patch>[-alpha-YYYY-MM-DD]`. They existed only
    // to answer the dated override's exit, the override is gone, and a mechanism
    // for a thing that no longer exists is not a test anybody can read.
    //
    // THE ORDERING IS NOT COMING BACK WIDER. @playwright/mcp 0.0.82 pins
    // `1.64.0-alpha-1789764292000`, a 13-digit epoch-milliseconds alpha that
    // `Orderable` refused by design rather than guessed at, and that refusal was
    // correct: an unorderable shape silently called 'lower' keeps an override
    // alive past its own exit. Nothing in this tree ranks a playwright-core
    // version now. Every comparison left is for IDENTITY, against another
    // recorded string, so a version is an opaque string and its shape is
    // upstream's business.

    private static JsonDocument ReadJson(string name) =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", name)));

    /// <summary>
    /// The fenced chain <c>DECISIONS.md</c> section 2 prints as what ships today.
    /// </summary>
    /// <returns>The contents of the block, without its fences.</returns>
    /// <exception cref="InvalidOperationException">The anchor or the block is gone.</exception>
    private static string WorkedExample()
    {
        var decisions = File.ReadAllText(Path.Combine(RepositoryLayout.Root.FullName, "DECISIONS.md"));
        var anchor = decisions.IndexOf(WorkedExampleAnchor, StringComparison.Ordinal);

        if (anchor < 0)
        {
            throw new InvalidOperationException(
                $"DECISIONS.md carries no '{WorkedExampleAnchor}' sentence, which is this check's anchor: the "
                + "worked example in section 2 was reworded or moved, and nothing here can say which chain it "
                + "prints as today's. Re-anchor this test on the new wording rather than deleting it.");
        }

        var fence = decisions.IndexOf(Fence, anchor, StringComparison.Ordinal);
        var start = fence < 0 ? -1 : decisions.IndexOf('\n', fence) + 1;
        var close = start <= 0 ? -1 : decisions.IndexOf(Fence, start, StringComparison.Ordinal);

        if (close < 0)
        {
            throw new InvalidOperationException(
                "DECISIONS.md's worked-example anchor is no longer followed by a fenced block, so section 2 "
                + "prints no chain for this check to read.");
        }

        return decisions[start..close];
    }

    /// <summary>Records one link of the worked example disagreeing with its source.</summary>
    /// <param name="disagreements">The running list.</param>
    /// <param name="example">The fenced chain.</param>
    /// <param name="pattern">What reads this link out of it.</param>
    /// <param name="what">The link, as the failure message should name it.</param>
    /// <param name="expected">What the committed record says.</param>
    /// <param name="source">Which committed record that is.</param>
    private static void Disagrees(
        List<string> disagreements,
        string example,
        Regex pattern,
        string what,
        string expected,
        string source)
    {
        var match = pattern.Match(example);

        if (!match.Success)
        {
            // Not the same failure as a wrong number and it must not read like
            // one: a pattern that stopped matching reports nothing at all, which
            // is how a scan goes quietly blind.
            disagreements.Add(
                $"DECISIONS.md section 2's worked example no longer states {what}, so nothing here can tell whether "
                + $"that link moved with the payload. {source} says '{expected}'.");

            return;
        }

        if (match.Groups["version"].Value != expected)
        {
            disagreements.Add(
                $"DECISIONS.md section 2's worked example says {what} '{match.Groups["version"].Value}' and "
                + $"{source} says '{expected}'. Correct the example by addition, with a stamp.");
        }
    }

    [GeneratedRegex(@"@playwright/mcp (?<version>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex WrapperInExample();

    [GeneratedRegex(@"└── playwright-core (?<version>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ResolvedCoreInExample();

    [GeneratedRegex(@"chromium rev (?<version>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ChromiumRevisionInExample();

    [GeneratedRegex(@"chromium rev \d+ \((?<version>[^)]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ChromiumBrowserVersionInExample();
}

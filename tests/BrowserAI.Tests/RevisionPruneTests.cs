// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// Pruning superseded browser revisions — the obligation
/// <c>PLAYWRIGHT_SKIP_BROWSER_GC=1</c> created and nothing discharged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every test here runs against a scratch browsers root, and that is a safety
/// requirement rather than hygiene.</b> The subject is code that deletes browser
/// trees; pointed at the developer's own <c>%LocalAppData%\BrowserAI\browsers</c>
/// a defect costs a 430 MiB re-download, and pointed at it by a test that ran
/// unattended it costs one nobody sees happen.
/// </para>
/// <para>
/// <b>The manifest is the real one.</b> What counts as <i>current</i> comes from
/// the resolved payload's <c>browsers.json</c>, so these tests move with a
/// revision bump instead of asserting a literal that stops being true — the same
/// reason <see cref="ProvisionedBrowsers"/> computes its paths rather than
/// spelling them.
/// </para>
/// </remarks>
internal sealed class RevisionPruneTests
{
    /// <summary>A revision nothing will ever name, so it is superseded by construction.</summary>
    private const string SupersededChromium = "chromium-1000";

    /// <summary>The same, for the other family, so one pass is proven to reach both.</summary>
    private const string SupersededFirefox = "firefox-1000";

    [Test]
    public async Task ASupersededRevisionIsDeletedAndTheOneTheManifestNamesIsKept()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("prune-superseded");

        var root = Path.Combine(scratch.Path, "browsers");
        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);

        var current = Plant(root, manifest.For(ProvisionedBrowsers.Chromium).DirectoryName, bytes: 4096);
        var oldChromium = Plant(root, SupersededChromium, bytes: 8192);
        var oldFirefox = Plant(root, SupersededFirefox, bytes: 2048);

        var report = RevisionPrune.Run(root, manifest, log.CreateLogger<RevisionPruneTests>());

        // The revision this build wants is the one thing a prune may never touch.
        await Assert.That(Directory.Exists(current)).IsTrue();

        await Assert.That(Directory.Exists(oldChromium)).IsFalse();
        await Assert.That(Directory.Exists(oldFirefox)).IsFalse();
        await Assert.That(report.Removed.Count).IsEqualTo(2);

        // Reclaimed is measured rather than counted: the whole reason this exists
        // is the disk, and "two directories" is not an amount of disk.
        await Assert.That(report.ReclaimedBytes).IsGreaterThanOrEqualTo(8192 + 2048);
        await Assert.That(report.Retained).IsEmpty();
    }

    [Test]
    public async Task ADirectoryThatIsNotAKnownBrowserRevisionIsNeverTouched()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("prune-unknown");

        var root = Path.Combine(scratch.Path, "browsers");
        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);

        // `.links` is playwright-core's own registry index and deleting it is what
        // makes upstream's GC prune a live tree — the exact hazard
        // PLAYWRIGHT_SKIP_BROWSER_GC=1 exists to stop, which this must not
        // reintroduce from the other side.
        var links = Plant(root, ".links", bytes: 32);

        // Velopack creates this beside things it owns, and a person may put
        // anything at all in a directory under their own LocalAppData.
        var velopack = Plant(root, "VelopackTemp", bytes: 64);
        var strange = Plant(root, "notabrowser-9999", bytes: 64);

        // The near miss that matters: a name that starts like a browser but is not
        // one. `chromiumish-1` does not begin with `chromium-`, and upstream's own
        // comment says why the check is written this way — `webkit` is a prefix of
        // `webkit-technology-preview`.
        var nearMiss = Plant(root, "chromiumish-1", bytes: 64);

        var report = RevisionPrune.Run(root, manifest, log.CreateLogger<RevisionPruneTests>());

        foreach (var kept in new[] { links, velopack, strange, nearMiss })
        {
            await Assert.That(Directory.Exists(kept)).IsTrue();
        }

        await Assert.That(report.Removed).IsEmpty();
        await Assert.That(report.ReclaimedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ARevisionSomethingIsRunningOutOfIsKeptAndTheProcessIsNamed()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("prune-in-use");
        using var scope = new JobObjectScope();

        var root = Path.Combine(scratch.Path, "browsers");
        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);
        var superseded = Plant(root, SupersededChromium, bytes: 1024);

        // A REAL process with a real image path inside the tree, because the guard
        // is a real enumeration of the machine's process list. A double here would
        // assert that the test's own stub says no.
        var (planted, imagePath) = await PlantedProcess.StartInAsync(scope, Path.Combine(superseded, "chrome-win64"), root);

        var report = RevisionPrune.Run(root, manifest, log.CreateLogger<RevisionPruneTests>());

        // The whole hazard in one assertion: superseded, and still there.
        await Assert.That(Directory.Exists(superseded)).IsTrue();
        await Assert.That(File.Exists(imagePath)).IsTrue();
        await Assert.That(report.Removed).IsEmpty();

        // And it says so rather than being silently skipped, with the pid somebody
        // can act on.
        await Assert.That(string.Join(Environment.NewLine, report.Retained)).Contains(planted.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(string.Join(Environment.NewLine, report.Retained)).Contains(SupersededChromium);
    }

    [Test]
    public async Task NothingIsPrunedWhileAnotherProcessIsProvisioning()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("prune-install-in-flight");

        var root = Path.Combine(scratch.Path, "browsers");
        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);
        var superseded = Plant(root, SupersededChromium, bytes: 1024);

        // Held on ANOTHER THREAD, because a named mutex is owned by the thread
        // that waited on it — taking it here would be re-entrant and would prove
        // nothing.
        using var taken = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var holder = new Thread(() =>
        {
            using var mutex = MachineMutex.Create(BrowserProvisioner.MutexNameFor(root, ProvisionedBrowsers.Chromium));
            _ = mutex.Acquire(TestDefaults.ProcessHang);
            taken.Set();
            _ = release.Wait(TestDefaults.ProcessHang);
            mutex.Release();
        })
        {
            IsBackground = true,
        };

        holder.Start();
        _ = taken.Wait(TestDefaults.ProcessHang);

        try
        {
            var report = RevisionPrune.Run(root, manifest, log.CreateLogger<RevisionPruneTests>());

            // An install in flight means a directory may be being written into
            // right now, so the pass declines as a whole rather than reasoning
            // about which directory is safe.
            await Assert.That(Directory.Exists(superseded)).IsTrue();
            await Assert.That(report.Removed).IsEmpty();
            await Assert.That(string.Join(Environment.NewLine, report.Retained)).Contains("provisioning");
        }
        finally
        {
            release.Set();
            _ = holder.Join(TestDefaults.ProcessHang);
        }
    }

    [Test]
    public async Task ASuccessfulProvisionPrunesWhatItSuperseded()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("prune-on-provision");

        var root = Path.Combine(scratch.Path, "browsers");
        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);
        var current = Path.Combine(root, manifest.For(ProvisionedBrowsers.Chromium).DirectoryName);
        var superseded = Plant(root, SupersededChromium, bytes: 4096);

        using var provisioner = new BrowserProvisioner(RepositoryPayload.Layout, root, log)
        {
            StartInstaller = (_, _) => FakeInstaller.Succeeding(current, TimeSpan.Zero),
        };

        var status = await provisioner.WaitAsync(ProvisionedBrowsers.Chromium);

        await Assert.That(status.State).IsEqualTo(ProvisioningState.Installed);

        // ⚠️ THE WIRING IS THE POINT. A pruner nothing calls is the state the
        // plan's final audit found: the obligation asserted in three places and
        // discharged in none. This asserts the call site, not the routine.
        await Assert.That(Directory.Exists(superseded)).IsFalse();
        await Assert.That(Directory.Exists(current)).IsTrue();
    }

    [Test]
    public async Task APruneThatThrowsLeavesTheProvisionItFollowedSuccessful()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var captured = new CapturingLoggerProvider();
        using var log = LoggerFactory.Create(builder =>
        {
            _ = builder.AddProvider(new TUnitLoggerProvider());
            _ = builder.AddProvider(captured);
        });

        using var scratch = ScratchDirectory.Create("prune-throws");

        var root = Path.Combine(scratch.Path, "browsers");
        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);
        var current = Path.Combine(root, manifest.For(ProvisionedBrowsers.Chromium).DirectoryName);

        using var provisioner = new BrowserProvisioner(RepositoryPayload.Layout, root, log)
        {
            StartInstaller = (_, _) => FakeInstaller.Succeeding(current, TimeSpan.Zero),

            // The prune runs INSIDE the install path's catch-all, which turns any
            // exception into a Failed status. Without a catch of its own, a disk
            // that would not give up an old tree would report a 203.8 MB download
            // that succeeded as a provisioning failure — and the caller would
            // download it again.
            PruneRevisions = _ => throw new IOException("The pruner fell over."),
        };

        var status = await provisioner.WaitAsync(ProvisionedBrowsers.Chromium);

        await Assert.That(status.State).IsEqualTo(ProvisioningState.Installed);
        await Assert.That(Directory.Exists(current)).IsTrue();

        // ⚠️ The status alone does NOT prove the guard, and the plant is what
        // showed it: `Peek` reads the completion marker before the cached result,
        // so an unguarded throw still answers `Installed`. What it also does is
        // write `Provisioning chromium failed` at Error over a download that
        // succeeded — a confident wrong answer in the one place this project
        // treats as evidence. Both halves are asserted.
        await Assert.That(captured.Records.Any(record => record.EventId.Id is 70)).IsFalse();

        // And it is not silent about it either: the disk an old revision holds
        // will not come back by waiting, so somebody has to be told.
        await Assert.That(captured.Logged("Pruning superseded browser revisions failed")).IsTrue();
        await Assert.That(captured.Records.Any(record => record.EventId.Id is 86 && record.Exception is IOException)).IsTrue();
    }

    [Test]
    public async Task TheDirectoryNameIsSpelledTheWayUpstreamSpellsIt()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);

        // ⚠️ Corrected 2026-08-17. `$"{Name}-{Revision}"` is right for both
        // families BrowserAI provisions and wrong for the headless shell, which
        // lands in `chromium_headless_shell-<rev>`: upstream computes the folder
        // as `name.replace(/-/g, "_") + "-" + revision`, so that a browser whose
        // name is a prefix of another's cannot claim its directory. It never
        // mattered while nothing walked the whole root; a pruner does.
        var shell = manifest.For("chromium-headless-shell");

        await Assert.That(shell.DirectoryName).StartsWith("chromium_headless_shell-");
        await Assert.That(shell.DirectoryName).DoesNotContain("chromium-headless-shell");

        // And the two families that are provisioned keep their dashes, because
        // their names carry none.
        await Assert.That(manifest.For(ProvisionedBrowsers.Chromium).DirectoryName)
            .IsEqualTo($"chromium-{manifest.For(ProvisionedBrowsers.Chromium).Revision}");

        // The whole manifest is the prune's vocabulary, so an entry it cannot see
        // is a tree it would delete. ffmpeg and winldd arrive with a chromium
        // install and are exactly the ones a narrow list would miss.
        var names = manifest.Entries.Select(entry => entry.Name).ToArray();

        await Assert.That(names).Contains("ffmpeg");
        await Assert.That(names).Contains("winldd");
        await Assert.That(manifest.Entries.Count).IsGreaterThanOrEqualTo(5);
    }

    [Test]
    public async Task TheCensusIsAskedAgainForEveryCandidateRatherThanOncePerPass()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("prune-census-per-candidate");

        var root = Path.Combine(scratch.Path, "browsers");
        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);

        var first = Plant(root, "chromium-1000", bytes: 1024);
        var second = Plant(root, "chromium-1001", bytes: 1024);

        // A browser that starts DURING the pass. It cannot be staged with a real
        // process -- the pass would have to be paused at an instant nothing can
        // reach -- so what is staged is the state it produces: a census that
        // answers "idle" the first time it is asked and "in use" afterwards. A
        // pass that asks once cannot tell the difference and deletes both.
        var asked = new List<string>();

        IReadOnlyList<RunningImage> census(string directory)
        {
            lock (asked)
            {
                asked.Add(directory);

                return asked.Count is 1
                    ? []
                    : [new RunningImage(4242, 0x01DC00000000000L, Path.Combine(directory, "chrome-win64", "chrome.exe"))];
            }
        }

        var report = RevisionPrune.Run(root, manifest, log.CreateLogger<RevisionPruneTests>(), census: census);

        // The first candidate was idle when it was asked about, so it went.
        await Assert.That(Directory.Exists(first)).IsFalse();
        await Assert.That(report.Removed).Contains(first);

        // The second was not, and only a second question could have said so.
        await Assert.That(Directory.Exists(second)).IsTrue();
        await Assert.That(report.Removed).DoesNotContain(second);

        var retained = string.Join(Environment.NewLine, report.Retained);

        await Assert.That(retained).Contains("chromium-1001");
        await Assert.That(retained).Contains("4242");

        // And the census was asked about each candidate by name, not once about
        // the root -- which is the shape that makes the freshness real rather
        // than an accident of when the one question was asked.
        await Assert.That(asked.Count).IsEqualTo(2);
        await Assert.That(asked).Contains(first);
        await Assert.That(asked).Contains(second);
        await Assert.That(asked).DoesNotContain(root);
    }

    [Test]
    public async Task ARevisionThatWouldNotDeleteSaysWhetherABrowserIsRunningOutOfIt()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("prune-would-not-go");

        var root = Path.Combine(scratch.Path, "browsers");
        var manifest = BrowsersManifest.Read(RepositoryPayload.Layout);
        var superseded = Plant(root, SupersededChromium, bytes: 1024);
        var held = Path.Combine(superseded, "chrome-win64", "payload.bin");

        // Idle when the decision was taken, in use by the time the delete had
        // half finished: the ordinary shape of the race, and the one whose
        // report used to read as a stuck file.
        var asked = 0;

        IReadOnlyList<RunningImage> census(string directory) =>
            Interlocked.Increment(ref asked) is 1
                ? []
                : [new RunningImage(4243, 0x01DC00000000000L, Path.Combine(directory, "chrome-win64", "chrome.exe"))];

        PruneReport report;

        using (var _ = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            report = RevisionPrune.Run(root, manifest, log.CreateLogger<RevisionPruneTests>(), census: census);
        }

        await Assert.That(Directory.Exists(superseded)).IsTrue();
        await Assert.That(report.Removed).IsEmpty();

        var retained = string.Join(Environment.NewLine, report.Retained);

        // The survivors are still listed -- that was never the defect.
        await Assert.That(retained).Contains("would not fully delete");
        await Assert.That(retained).Contains("payload.bin");

        // The diagnosis is what was missing: a live browser whose tree has just
        // been gutted, not a file a scanner is holding.
        await Assert.That(retained).Contains("RIGHT NOW");
        await Assert.That(retained).Contains("4243");
        await Assert.That(asked).IsEqualTo(2);
    }

    /// <summary>
    /// Lays down a directory of a given size under the browsers root, complete
    /// with the marker a real install writes last.
    /// </summary>
    /// <param name="root">The browsers root.</param>
    /// <param name="name">The revision directory's leaf name.</param>
    /// <param name="bytes">How much to put in it, so reclaimed disk is measurable.</param>
    /// <returns>The directory's absolute path.</returns>
    private static string Plant(string root, string name, int bytes)
    {
        var directory = Path.Combine(root, name);

        _ = Directory.CreateDirectory(Path.Combine(directory, "chrome-win64"));
        File.WriteAllBytes(Path.Combine(directory, "chrome-win64", "payload.bin"), new byte[bytes]);
        File.WriteAllText(Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker), string.Empty);

        return directory;
    }
}

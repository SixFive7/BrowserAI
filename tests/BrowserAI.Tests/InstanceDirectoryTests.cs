// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using BrowserAI.Hosting;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The per-run directory, and the sweep that exists because the containment
/// contract guarantees the tidy-up path sometimes does not run.
/// </summary>
/// <remarks>
/// A cleanup that only happens in a <c>finally</c> is a cleanup that will one
/// day not happen — and here it is not a matter of chance: BrowserAI is designed
/// to be killable from outside without running any code, and the acceptance test
/// for the job object kills it on every run. That is what turned nineteen suite
/// runs into nineteen abandoned directories, which is how this sweep came to
/// exist.
/// </remarks>
internal sealed class InstanceDirectoryTests
{
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    private static readonly TimeSpan ReadyPatience = TimeSpan.FromSeconds(30);

    [Test]
    public async Task AFreshDirectoryIsCreatedAndAbandonedOnesAreReclaimed()
    {
        using var scratch = ScratchDirectory.Create("instance-sweep");
        var paths = new LocalAppDataPaths(scratch.Path);

        var abandoned = Path.Combine(paths.InstanceRoot, "1234-abandoned");
        _ = Directory.CreateDirectory(abandoned);
        await File.WriteAllTextAsync(Path.Combine(abandoned, "playwright-mcp.config.json"), "{}");
        Directory.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-1));

        var created = InstanceDirectory.CreateFresh(paths, NullLogger.Instance);

        await Assert.That(Directory.Exists(created)).IsTrue();
        await Assert.That(Directory.Exists(abandoned)).IsFalse();

        // Nothing is left renamed aside either: the claim is a step on the way
        // to a delete, not a rubbish heap the next run has to recognise.
        await Assert.That(Directory.EnumerateDirectories(paths.InstanceRoot).Count()).IsEqualTo(1);

        // Under the root the app-paths seam names, so the swap to Velopack's
        // locator at step 19 moves it without anything else changing.
        await Assert.That(created).StartsWith(paths.InstanceRoot);
    }

    [Test]
    public async Task ADirectoryThatMayStillBeStartingIsLeftAlone()
    {
        using var scratch = ScratchDirectory.Create("instance-sweep-young");
        var paths = new LocalAppDataPaths(scratch.Path);

        // The one gap the working-directory lock does not cover: the instants
        // between a run creating its directory and its child adopting it as a
        // cwd. Anything touched recently is skipped rather than raced.
        var young = Path.Combine(paths.InstanceRoot, "5678-just-started");
        _ = Directory.CreateDirectory(young);

        _ = InstanceDirectory.CreateFresh(paths, NullLogger.Instance);

        await Assert.That(Directory.Exists(young)).IsTrue();
    }

    /// <summary>
    /// The clean exit path meets a file something still holds, deletes
    /// everything else, and <b>names what survived</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="Runtime.TreeDelete"/> exists for exactly this caller.</b> An
    /// instance directory has just held a
    /// running browser, and Chromium leaves mapped files behind for a moment
    /// after exit — the race is the normal case rather than the unlucky one.
    /// </para>
    /// <para>
    /// <b>The assertion that matters is the log line, not the survivor.</b>
    /// Measured 2026-08-16 on .NET 10.0.11: <c>Directory.Delete(path, recursive:
    /// true)</c> leaves the same nodes behind that the per-node walk does, so an
    /// on-disk assertion alone passes against the primitive §E forbids. What it
    /// cannot produce is the list — it throws one exception naming one node, and
    /// this file used to swallow it whole.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADirectoryWithAFileSomethingHoldsIsEmptiedAroundItAndTheSurvivorIsNamed()
    {
        using var scratch = ScratchDirectory.Create("instance-held");
        using var provider = new CapturingLoggerProvider();

        var instance = Path.Combine(scratch.Path, "instance");
        var profile = Path.Combine(instance, "profile", "Default");
        _ = Directory.CreateDirectory(profile);
        _ = Directory.CreateDirectory(Path.Combine(instance, "output"));

        await File.WriteAllTextAsync(Path.Combine(instance, "playwright-mcp.config.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(instance, "output", "page-1.png"), "x");
        await File.WriteAllTextAsync(Path.Combine(profile, "sibling.bin"), "x");

        var held = Path.Combine(profile, "held.bin");
        await File.WriteAllTextAsync(held, "x");

        // FileShare.None, in this process: what a mapped Chromium file looks
        // like to a delete, without needing a browser to produce one.
        using (var holder = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            InstanceDirectory.Delete(instance, provider.CreateLogger("BrowserAI.Tests"));
        }

        // Everything that could go, went -- including the two directories that
        // sit after the held one in enumeration order.
        await Assert.That(File.Exists(Path.Combine(instance, "playwright-mcp.config.json"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(instance, "output"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(profile, "sibling.bin"))).IsFalse();
        await Assert.That(File.Exists(held)).IsTrue();

        // And the run said so, naming the file rather than the operation. This
        // is the half a swallow-all catch cannot have.
        var reported = provider.Records
            .Where(record => record.Level >= LogLevel.Warning)
            .Select(record => record.Message)
            .ToList();

        await Assert.That(reported).IsNotEmpty();
        await Assert.That(string.Join(Environment.NewLine, reported)).Contains("held.bin");
    }

    /// <summary>
    /// A run that is still going keeps its instance directory, contents and all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured 2026-08-16, twice, and it is why the claim is a rename.</b>
    /// Windows refuses to remove a directory that is a live process's current
    /// directory — but it does not refuse to delete the files <i>inside</i> it,
    /// so <c>Directory.Delete(path, recursive: true)</c> emptied a live run's
    /// instance directory completely and only then failed on the node. The
    /// generated config, the surface child's profile, the output folder and the
    /// downloads folder were all gone and nothing was reported, on every startup,
    /// against any instance older than five minutes.
    /// </para>
    /// <para>
    /// The probe holds a file <b>outside</b> the directory deliberately, so the
    /// only thing standing between the sweep and the tree is the working-
    /// directory lock this test is about.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARunThatIsStillGoingKeepsItsInstanceDirectoryAndEverythingInIt()
    {
        using var scratch = ScratchDirectory.Create("instance-live");
        using var scope = new JobObjectScope();
        using var provider = new CapturingLoggerProvider();

        var paths = new LocalAppDataPaths(scratch.Path);

        var live = Path.Combine(paths.InstanceRoot, "4321-still-running");
        var profile = Path.Combine(live, "profile");
        _ = Directory.CreateDirectory(profile);

        var config = Path.Combine(live, "playwright-mcp.config.json");
        var preferences = Path.Combine(profile, "Preferences");
        await File.WriteAllTextAsync(config, "{}");
        await File.WriteAllTextAsync(preferences, "{}");

        // Old enough to be swept. The age guard is the only other thing that
        // could keep this tree, and leaving it in play would make the test pass
        // for the wrong reason.
        Directory.SetLastWriteTimeUtc(live, DateTime.UtcNow.AddHours(-1));

        var ready = Path.Combine(scratch.Path, "ready.json");
        var elsewhere = Path.Combine(scratch.Path, "held-outside.bin");

        _ = scope.Launch(ProbePath, live, "hold-file", elsewhere, ready);
        await WaitForFileAsync(ready);

        _ = InstanceDirectory.CreateFresh(paths, provider.CreateLogger("BrowserAI.Tests"));

        await Assert.That(Directory.Exists(live)).IsTrue();
        await Assert.That(File.Exists(config)).IsTrue();
        await Assert.That(File.Exists(preferences)).IsTrue();

        // Nothing was renamed aside either -- a claim that half-succeeded would
        // leave the live child's cwd under a name it never agreed to.
        await Assert.That(Directory.EnumerateDirectories(paths.InstanceRoot)
            .Select(Path.GetFileName)
            .Any(name => name!.StartsWith("sweeping-", StringComparison.Ordinal)))
            .IsFalse();
    }

    private static async Task WaitForFileAsync(string path)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < ReadyPatience)
        {
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"The probe never wrote '{path}', so nothing was holding the directory under test.");
    }
}

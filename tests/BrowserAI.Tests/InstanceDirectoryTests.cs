// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

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
    [Test]
    public async Task AFreshDirectoryIsCreatedAndAbandonedOnesAreReclaimed()
    {
        using var scratch = ScratchDirectory.Create("instance-sweep");
        var paths = new LocalAppDataPaths(scratch.Path);

        var abandoned = Path.Combine(paths.InstanceRoot, "1234-abandoned");
        _ = Directory.CreateDirectory(abandoned);
        await File.WriteAllTextAsync(Path.Combine(abandoned, "playwright-mcp.config.json"), "{}");
        Directory.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-1));

        var created = InstanceDirectory.CreateFresh(paths);

        await Assert.That(Directory.Exists(created)).IsTrue();
        await Assert.That(Directory.Exists(abandoned)).IsFalse();

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

        _ = InstanceDirectory.CreateFresh(paths);

        await Assert.That(Directory.Exists(young)).IsTrue();
    }
}

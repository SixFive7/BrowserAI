// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// <c>.work\installer.lock</c>: the suite takes it, a gate driver declares it, and
/// both halves speak one protocol.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Q291, decided 2026-09-24 by the maintainer, verbatim: <i>"Q291 a"</i>.</b>
/// Until that day the lock was a convention: nothing in the tree read it, and a run
/// that included the installer arms had to be started by somebody who had taken it by
/// hand. The suite takes it now from a session hook, waits boundedly for a live
/// holder, takes over from a dead one, and lets it go when the session ends; a gate
/// driver takes it first and declares itself, so nothing deadlocks.
/// </para>
/// <para>
/// <b>Planted red 2026-09-24</b>: <see cref="ThisRunHoldsTheInstallerLock"/> against
/// a tree whose session hook did not take it yet read the state as unread.
/// </para>
/// </remarks>
internal sealed class InstallerLockTests
{
    /// <summary>A holder with a creation time, the shape this protocol writes.</summary>
    private static readonly InstallerLockHolder Somebody = new(4242, 133_700_000_000_000_000);

    /// <summary>
    /// What one reading of the lock says to do follows from what was read, in every
    /// case the protocol names.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachReadingOfTheLockLeadsToTheStepTheProtocolNames()
    {
        var line = "holder=gate " + InstallerLock.TokenOf(Somebody) + " at=2026-09-24T17:00:00Z\n";
        var old = TimeSpan.FromMinutes(1);

        // No file: take it.
        await Assert.That(InstallerLock.Decide(null, null, _ => true, old)).IsEqualTo(InstallerLockStep.Create);

        // A live holder nobody told this process about: wait.
        await Assert.That(InstallerLock.Decide(line, null, _ => true, old)).IsEqualTo(InstallerLockStep.Wait);

        // The live holder this process was told about: covered, neither waited for
        // nor let go -- a gate driver, or the host that started a child host.
        await Assert.That(InstallerLock.Decide(line, InstallerLock.TokenOf(Somebody), _ => true, old)).IsEqualTo(InstallerLockStep.Covered);

        // Told about a DIFFERENT holder than the one in the file: wait, and never
        // covered on somebody else's word.
        await Assert.That(InstallerLock.Decide(line, InstallerLock.TokenOf(Somebody with { ProcessId = 4243 }), _ => true, old))
            .IsEqualTo(InstallerLockStep.Wait);

        // A holder that is gone, declared or not: stale, taken over.
        await Assert.That(InstallerLock.Decide(line, null, _ => false, old)).IsEqualTo(InstallerLockStep.Stale);
        await Assert.That(InstallerLock.Decide(line, InstallerLock.TokenOf(Somebody), _ => false, old)).IsEqualTo(InstallerLockStep.Stale);

        // A file with no holder in it: a holder mid-write for a moment, debris after.
        await Assert.That(InstallerLock.Decide(string.Empty, null, _ => true, TimeSpan.Zero)).IsEqualTo(InstallerLockStep.Wait);
        await Assert.That(InstallerLock.Decide(string.Empty, null, _ => true, old)).IsEqualTo(InstallerLockStep.Stale);

        // The two spellings of a holder: this protocol's, and the one the rigs that
        // took the file by hand wrote, which carries no creation time.
        await Assert.That(InstallerLock.Parse(line)).IsEqualTo(Somebody);
        await Assert.That(InstallerLock.Parse("coordinator-lifecycle researcher, pid 1234, 2026-09-24T12:00:00Z"))
            .IsEqualTo(new InstallerLockHolder(1234, null));
        await Assert.That(InstallerLock.Parse("nothing here")).IsNull();
    }

    /// <summary>This run holds the lock: it took it, or a live holder that started it did.</summary>
    /// <remarks>
    /// <b>The premise every installer arm rests on</b>, asserted the way
    /// <c>WindowWatchTests.ThisRunIsWatched</c> asserts its own: a run that could not
    /// hold the lock skips those arms, and this is what says the lock was not simply
    /// never asked for.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThisRunHoldsTheInstallerLock()
    {
        await Assert.That(InstallerLock.State is InstallerLockState.Taken or InstallerLockState.Declared)
            .IsTrue()
            .Because($"{InstallerLock.State}: {InstallerLock.Detail}");

        await Assert.That(InstallerLock.CoverageRow).Contains(InstallerLock.Title);

        // Held means the file is there and names a live holder.
        var text = await File.ReadAllTextAsync(InstallerLock.FilePath);

        await Assert.That(InstallerLock.Parse(text) is { } holder && InstallerLock.IsAlive(holder)).IsTrue().Because(text);
    }

    /// <summary>
    /// The drivers' half of the lock writes what the suite's half reads, waits for a
    /// live holder, takes over from a dead one, and lets go only of its own.
    /// </summary>
    /// <remarks>
    /// <b>Driven against a scratch lock file</b>, never the real one, with this test
    /// host standing in as the live holder and a finished child standing in as the
    /// dead one.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDriversHalfWritesWhatTheSuitesHalfReads()
    {
        using var scratch = ScratchDirectory.Create("installer-lock");
        var lockFile = Path.Combine(scratch.Path, "installer.lock");
        var me = Environment.ProcessId;

        // ---- Take, for a live holder: the token on stdout is what the suite parses.
        var (taken, token) = await RunAsync("-Take", "-HolderPid", me.ToString(CultureInfo.InvariantCulture), "-Path", lockFile);

        await Assert.That(taken).IsEqualTo(0);
        await Assert.That(InstallerLock.Parse(token)).IsEqualTo(new InstallerLockHolder(me, ProcessIdentity.CreationTimeOf(me)));
        await Assert.That(InstallerLock.Parse(await File.ReadAllTextAsync(lockFile))).IsEqualTo(InstallerLock.Parse(token));

        // ---- Another holder waits for a live one, and gives up at its wait. This
        // host's own parent is a second live process for as long as this arm runs.
        var other = ParentProcess.IdOf(me);
        var (refused, _) = await RunAsync("-Take", "-HolderPid", other.ToString(CultureInfo.InvariantCulture), "-Path", lockFile, "-WaitSeconds", "1");

        await Assert.That(refused).IsEqualTo(1);

        // ---- Letting go is only for the holder the file names.
        _ = await RunAsync("-Release", "-HolderPid", other.ToString(CultureInfo.InvariantCulture), "-Path", lockFile);

        await Assert.That(File.Exists(lockFile)).IsTrue();

        _ = await RunAsync("-Release", "-HolderPid", me.ToString(CultureInfo.InvariantCulture), "-Path", lockFile);

        await Assert.That(File.Exists(lockFile)).IsFalse();

        // ---- A holder that is gone is taken over. A child that has exited is a pid
        // and a creation time that no longer name a live process.
        var dead = await ExitedChildAsync();

        await File.WriteAllTextAsync(lockFile, "holder=gate " + InstallerLock.TokenOf(dead) + " at=2026-09-24T00:00:00Z\n");

        var (overTaken, overToken) = await RunAsync("-Take", "-HolderPid", me.ToString(CultureInfo.InvariantCulture), "-Path", lockFile, "-WaitSeconds", "60");

        await Assert.That(overTaken).IsEqualTo(0);
        await Assert.That(InstallerLock.Parse(await File.ReadAllTextAsync(lockFile))).IsEqualTo(InstallerLock.Parse(overToken));

        _ = await RunAsync("-Release", "-HolderPid", me.ToString(CultureInfo.InvariantCulture), "-Path", lockFile);
    }

    /// <summary>A process that has exited, as the pid and creation time it had.</summary>
    private static async Task<InstallerLockHolder> ExitedChildAsync()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("exit 0");

        using var child = Process.Start(start) ?? throw new InvalidOperationException("cmd.exe did not start.");

        var created = child.StartTime.ToFileTimeUtc();
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();

        await child.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);
        _ = await output;
        _ = await error;

        return new InstallerLockHolder(child.Id, created);
    }

    /// <summary>Runs the drivers' lock script and returns its exit code and stdout.</summary>
    private static async Task<(int Exit, string Output)> RunAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // The house rule for every launch in this tree.
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(RepositoryLayout.Root.FullName, "build", "InstallerLock.ps1"));

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("'pwsh' did not start for the lock script.");

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);
        _ = await error;

        return (process.ExitCode, (await output).Trim());
    }
}

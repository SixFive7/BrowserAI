// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The one canonicalisation function, tested to agree with itself.
/// </summary>
/// <remarks>
/// The mutex name, the lock file and the session index all key on the same
/// directory. If any two of them normalise differently the same directory
/// acquires two identities, and a lock keyed on one of them reports success
/// while guarding nothing — which is why this is a done-test of its own rather
/// than a property of the lock.
/// </remarks>
internal sealed class SessionPathTests
{
    [Test]
    public async Task ThreeSpellingsOfOneDirectoryProduceOneIdentity()
    {
        using var scratch = ScratchDirectory.Create("session-identity");

        // Mixed case on purpose: the case-folding half of the chain is what
        // makes `c:\a` and `C:\A` one session rather than two.
        var directory = Path.Combine(scratch.Path, "Session One");
        _ = Directory.CreateDirectory(directory);

        var plain = SessionPath.Resolve(directory);
        var trailing = SessionPath.Resolve(directory + Path.DirectorySeparatorChar);
        var cased = SessionPath.Resolve(directory.ToUpperInvariant());
        var dotted = SessionPath.Resolve(Path.Combine(scratch.Path, "elsewhere", "..", "Session One"));

        SessionPath[] spellings = [plain, trailing, cased, dotted];

        foreach (var spelling in spellings)
        {
            await Assert.That(spelling.MutexName).IsEqualTo(plain.MutexName);
            await Assert.That(spelling.IndexKey).IsEqualTo(plain.IndexKey);
            await Assert.That(spelling.Key).IsEqualTo(plain.Key);
        }

        // "One file path" is asserted as one file on disk rather than as one
        // string, and that is the stronger claim of the two. FullPath keeps the
        // caller's casing deliberately -- Windows has supported per-directory
        // case sensitivity since 1803, so an upper-cased path used for real I/O
        // would send a caller to a directory that does not exist. What has to be
        // true is that every spelling lands on the same file, so that is what is
        // measured.
        await File.WriteAllTextAsync(plain.LockFile, "{}");

        foreach (var spelling in spellings)
        {
            await Assert.That(File.Exists(spelling.LockFile)).IsTrue();
            await Assert.That(string.Equals(spelling.LockFile, plain.LockFile, StringComparison.OrdinalIgnoreCase)).IsTrue();
        }

        await Assert.That(Directory.GetFiles(directory).Length).IsEqualTo(1);
    }

    [Test]
    public async Task ARelativeSpellingCanonicalisesTheSameWay()
    {
        using var scratch = ScratchDirectory.Create("session-identity-relative");

        var directory = Path.Combine(scratch.Path, "Session One");
        _ = Directory.CreateDirectory(directory);

        var absolute = SessionPath.Resolve(directory);

        // From a process whose current directory is the parent. The host's own
        // current directory is fixed and shared by every test running in
        // parallel, so the relative case is answered by a process that can
        // legitimately have a different one.
        var run = await ProbeProcess.RunInAsync(scratch.Path, "session-identity", @".\Session One", Path.Combine(scratch.Path, "relative.json"));

        await Assert.That(run.ExitCode).IsEqualTo(0);

        var report = await ProbeReport.ReadAsync(Path.Combine(scratch.Path, "relative.json"), TestDefaults.ProcessHang);

        await Assert.That((string?)report["workingDirectory"]).IsEqualTo(scratch.Path);
        await Assert.That((string?)report["mutexName"]).IsEqualTo(absolute.MutexName);
        await Assert.That((string?)report["indexKey"]).IsEqualTo(absolute.IndexKey);
        await Assert.That((string?)report["key"]).IsEqualTo(absolute.Key);
    }

    [Test]
    public async Task TheMutexNameIsGlobalAndCarriesNoPathSeparator()
    {
        using var scratch = ScratchDirectory.Create("session-mutex-name");
        var path = SessionPath.Resolve(scratch.Path);

        await Assert.That(path.MutexName).StartsWith(@"Global\BrowserAI-");

        // A backslash after the Global\ prefix is illegal, which is the whole
        // reason the path is hashed rather than used.
        await Assert.That(path.MutexName[@"Global\".Length..]).DoesNotContain(@"\");
        await Assert.That(path.MutexName.Length).IsEqualTo(@"Global\BrowserAI-".Length + 32);

        // The full digest for the index, half of it for the mutex, and the two
        // derived from one hash rather than from two hashings.
        await Assert.That(path.IndexKey.Length).IsEqualTo(64);
        await Assert.That(path.IndexKey).StartsWith(path.MutexName[@"Global\BrowserAI-".Length..]);
    }

    [Test]
    public async Task AVolumeRootIsNotASessionDirectory()
    {
        // `C:\` trims to `C:`, which is a drive-relative path meaning "the
        // current directory on C:" -- a different directory that changes under
        // the caller's feet. Refused rather than silently accepted.
        var root = Assert.Throws<ArgumentException>(() => _ = SessionPath.Resolve(@"C:\"));
        await Assert.That(root!.Message).Contains("volume root");

        _ = Assert.Throws<ArgumentException>(() => _ = SessionPath.Resolve("   "));
    }
}

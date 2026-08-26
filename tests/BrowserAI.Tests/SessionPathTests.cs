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

        var plain = SessionPath.For(directory);
        var trailing = SessionPath.For(directory + Path.DirectorySeparatorChar);
        var cased = SessionPath.For(directory.ToUpperInvariant());

        // ⚠️ THE `..` SPELLING MOVED RATHER THAN WENT — 2026-08-26, previously a
        // fourth entry here built from `Path.Combine(scratch.Path, "elsewhere",
        // "..", "Session One")`. This type normalises nothing now: it derives
        // names from a path `CanonicalPath` has already answered for, so
        // collapsing a `..` here would be the second spelling of the chain its
        // own remarks forbid. `CanonicalPathTests` asserts that spelling, beside
        // every other alias this machine can build.
        SessionPath[] spellings = [plain, trailing, cased];

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
    public async Task ARelativeSpellingIsRefusedFromAProcessItWouldHaveResolvedIn()
    {
        // ⚠️ INVERTED 2026-08-26, and the inversion is the point (previously
        // "ARelativeSpellingCanonicalisesTheSameWay", which asserted that
        // `.\Session One` from the parent directory produced the same mutex,
        // index key and identity as the absolute spelling). It did — and that is
        // exactly the property that must not exist. A relative path resolves
        // against THIS process's working directory, which is a different
        // directory per process and never the one the caller meant, and the tools
        // have refused one for as long as they have existed. The chain underneath
        // them resolved it anyway, so the refusal was a property of the door
        // rather than of the path.
        //
        // Asserted from a process whose current directory really is the one the
        // relative spelling would have resolved against, because that is the only
        // arrangement in which "it was refused" is a claim about the rule rather
        // than about the spelling failing to resolve at all. The test host's own
        // current directory is fixed and shared by every test running in
        // parallel.
        using var scratch = ScratchDirectory.Create("session-identity-relative");

        var directory = Path.Combine(scratch.Path, "Session One");
        _ = Directory.CreateDirectory(directory);

        var report = Path.Combine(scratch.Path, "relative.json");
        var run = await ProbeProcess.RunInAsync(scratch.Path, "session-identity", @".\Session One", report);

        await Assert.That(run.ExitCode).IsEqualTo(0);

        var relative = await ProbeReport.ReadAsync(report, TestDefaults.ProcessHang);

        // Case-insensitive: the child reported its own Environment.CurrentDirectory,
        // which comes back through GetCurrentDirectoryW, while `scratch.Path` is
        // composed in this process from a root carrying whatever drive-letter
        // case the invoking shell handed the test host. See DriveLetterCase.
        await Assert.That((string?)relative["workingDirectory"]).IsEqualTo(scratch.Path, StringComparison.OrdinalIgnoreCase);
        await Assert.That((string?)relative["canonical"]).IsNull();
        await Assert.That((string?)relative["refusal"]).Contains("must be an absolute local path");

        // The positive control, in the same process and the same working
        // directory: the absolute spelling of the very same directory is
        // accepted, and its identity is the one this host derives. Without it,
        // the refusal above is satisfied by a probe that refuses everything.
        var absolute = Path.Combine(scratch.Path, "absolute.json");

        await Assert.That((await ProbeProcess.RunInAsync(scratch.Path, "session-identity", directory, absolute)).ExitCode).IsEqualTo(0);

        var answered = await ProbeReport.ReadAsync(absolute, TestDefaults.ProcessHang);

        await Assert.That((string?)answered["refusal"]).IsNull();
        await Assert.That((string?)answered["mutexName"]).IsEqualTo(SessionPath.For(directory).MutexName);
        await Assert.That((string?)answered["indexKey"]).IsEqualTo(SessionPath.For(directory).IndexKey);
        await Assert.That((string?)answered["key"]).IsEqualTo(SessionPath.For(directory).Key);
    }

    [Test]
    public async Task TheMutexNameIsGlobalAndCarriesNoPathSeparator()
    {
        using var scratch = ScratchDirectory.Create("session-mutex-name");
        var path = SessionPath.For(scratch.Path);

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
        //
        // ⚠️ It is refused HERE and not in the canonicaliser, and the split is
        // what makes `browserai_list` able to use one path chain: a volume root
        // is a perfectly good subtree to list and never a session directory.
        // CanonicalPathTests owns the other half.
        var root = Assert.Throws<ArgumentException>(() => _ = SessionPath.For(@"C:\"));
        await Assert.That(root!.Message).Contains("volume root");

        _ = Assert.Throws<ArgumentException>(() => _ = SessionPath.For("   "));
    }
}

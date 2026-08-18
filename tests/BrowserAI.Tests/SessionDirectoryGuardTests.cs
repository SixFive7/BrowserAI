// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The two things a session directory may not be, proved against aliases this
/// machine actually builds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every alias below is real.</b> A junction comes from <c>mklink /J</c>, a
/// short name from <c>GetShortPathNameW</c>, and the <c>subst</c> and mapped
/// drives from <c>DefineDosDeviceW</c> — the same call <c>subst</c> and the
/// multiple-UNC provider make. None of the four needs administrator rights, so
/// none of them is skipped or asserted against a string the test wrote itself,
/// which is the failure mode a guard like this invites: a predicate tested only
/// on inputs the test invented is a predicate tested against its own author's
/// idea of the alias.
/// </para>
/// <para>
/// ⚠️ <b>What is asserted is which branch answered, never the clock.</b> The
/// failure these refusals prevent is a 22-second stall, so a stopwatch is the
/// obvious assertion and it is the wrong one twice over: it is a promptness
/// claim a loaded machine can fail while the product is correct, and no bound
/// with headroom a 419-test run cannot reach is also tight enough to catch one
/// 22-second stall. This is the same correction
/// <c>StraySweepTests.AUncTitleIsRefusedByAStringCheckAndTheSweepStaysFast</c>
/// already carries, applied before the same mistake was made again — and
/// <c>TheNetworkRefusalDoesNotComeFromTheCallThatOpensThings</c> says what its
/// substitute can and cannot establish.
/// </para>
/// </remarks>
internal sealed class SessionDirectoryGuardTests
{
    /// <summary>
    /// A share that cannot be reached, chosen so that reaching it is cheap.
    /// </summary>
    /// <remarks>
    /// <b>An unroutable ADDRESS rather than a hostname that does not resolve,
    /// and the difference is twenty-two seconds.</b> Measured 2026-08-19: a
    /// dead hostname fails at DNS and costs <b>22,210 ms</b> through a mapped
    /// letter, while this address failed in <b>12.8 ms</b>
    /// ([kb](../../kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds)).
    /// One arm below deliberately DOES open a path through the mapping, to prove
    /// that the opening call cannot answer there — so the cheap one is the only
    /// one this suite can afford.
    /// </remarks>
    private const string UnreachableShare = @"10.255.255.1\share";

    [Test]
    public async Task AUncSessionDirectoryIsRefusedOnItsCharactersAlone()
    {
        // Four spellings, all of them fully qualified, none of them touched by
        // Path.GetFullPath, and all four refused before any syscall at all.
        string[] spellings =
        [
            @"\\10.255.255.1\share\session",
            @"//10.255.255.1/share/session",
            @"\\?\UNC\10.255.255.1\share\session",
            @"\\.\UNC\10.255.255.1\share\session",
        ];

        foreach (var spelling in spellings)
        {
            var location = SessionPath.Resolve(spelling);
            var refusal = SessionDirectoryGuard.Refuse("directory", location);

            await Assert.That(refusal).IsNotNull();
            await Assert.That(refusal!).Contains("is on a network path");

            // Named, so the caller can see which of several paths it was.
            await Assert.That(refusal).Contains(location.FullPath);
        }
    }

    [Test]
    public async Task AMappedDriveLetterIsRefusedAsANetworkPathThoughItIsSpelledLocally()
    {
        // THE WHOLE POINT OF THE PAIR ABOVE AND BELOW. `Z:\work` passes every
        // string test there is -- it is a rooted local drive-letter path by
        // every character in it -- and it resolves through the same redirector
        // and costs the same 22 seconds. A guard that closed the UNC spelling
        // and stopped there would read as complete and leave this open.
        using var mapped = DosDeviceAlias.MappedTo(UnreachableShare);

        var location = SessionPath.Resolve(mapped.PathTo("session"));
        var refusal = SessionDirectoryGuard.Refuse("directory", location);

        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!).Contains("is on a network path");

        // The clause is what makes this fixable: told only "that is a network
        // path" about a drive letter, a caller would reasonably think BrowserAI
        // had it wrong.
        await Assert.That(refusal).Contains($"drive '{mapped.Letter}' is a mapped network drive");

        // And the positive control, without which the assertion above proves
        // only that SOMETHING was refused: the same shape on a local letter is
        // accepted.
        using var scratch = ScratchDirectory.Create("guard-local-control");

        await Assert.That(SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(Path.Combine(scratch.Path, "session")))).IsNull();
    }

    [Test]
    public async Task TheNetworkRefusalDoesNotComeFromTheCallThatOpensThings()
    {
        // ⚠️ **This is the arm a stopwatch would have been written for, and the
        // stopwatch was deliberately not written.** What wants proving is that
        // the guard answers the network question BEFORE the one call in it that
        // opens a directory -- and elapsed time is the only witness to an
        // ordering, which makes it a promptness assertion a starved machine can
        // fail while the product is correct. TESTING.md's rule is that a
        // duration is a hang detector taken from TestDefaults or it is a defect,
        // and no hang detector with headroom a 419-test run cannot reach is also
        // tight enough to catch a single 22-second stall.
        //
        // So what is asserted instead is WHICH BRANCH ANSWERED, decisively:
        // through a mapped drive the opening call cannot produce an answer at
        // all -- it returns null, which the guard treats as "accepted,
        // unverified" -- so a refusal proves the object-manager branch is what
        // produced it.
        //
        // What this still cannot say is that the open did not ALSO happen, 22
        // seconds before the right answer arrived. That gap is real and is named
        // here rather than papered over; what stands against it is
        // VolumeIdentity's own rule that FinalNameOf is never called on a path
        // Of has not already found local, and the kb row that measures why.
        using var mapped = DosDeviceAlias.MappedTo(UnreachableShare);

        var location = SessionPath.Resolve(mapped.PathTo("session"));

        await Assert.That(VolumeIdentity.Of(location.FullPath).Kind).IsEqualTo(VolumeKind.Network);
        await Assert.That(VolumeIdentity.FinalNameOf(location.FullPath)).IsNull();

        var refusal = SessionDirectoryGuard.Refuse("directory", location);

        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!).Contains("is on a network path");

        // The control for the middle assertion: FinalNameOf really does answer
        // for a directory it can open, so "it returned null" above is about the
        // share rather than about the call being broken.
        using var scratch = ScratchDirectory.Create("guard-final-name-control");

        await Assert.That(VolumeIdentity.FinalNameOf(scratch.Path)).IsNotNull();
    }

    [Test]
    public async Task EveryAliasPathGetFullPathLeavesAloneIsRefusedWithTheAcceptedSpelling()
    {
        using var scratch = ScratchDirectory.Create("guard-aliases");

        // A name with a space in it, so this volume's 8.3 generator has
        // something to shorten.
        var real = Path.Combine(scratch.Path, "a real session directory");
        _ = Directory.CreateDirectory(real);

        var canonical = SessionPath.Resolve(real).FullPath;

        // 1. The device-namespace prefix. Needs no filesystem setup at all,
        //    which is what makes it the one an unlucky caller reaches first --
        //    and `Path.GetFullPath` passes an extended-length path through
        //    untouched, so it arrives here exactly as the caller typed it.
        await RefusedAsAnAlias(VolumeIdentity.ExtendedLengthPrefix + real, canonical);

        // 2. A junction. mklink /J needs no privilege and no Developer Mode,
        //    where Directory.CreateSymbolicLink needs SeCreateSymbolicLink.
        var link = Path.Combine(scratch.Path, "junction");
        await PathAliases.JunctionAsync(link, real);
        await RefusedAsAnAlias(link, canonical);

        // 3. A subst drive. Answered off the object manager without a single
        //    filesystem call, because the DOS device target IS the accepted
        //    spelling.
        using var substituted = DosDeviceAlias.Substituting(real);

        var refusal = SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(substituted.PathTo("page.png")));

        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!).Contains($"directory='{Path.Combine(real, "page.png")}'");
        await Assert.That(refusal).Contains($"drive '{substituted.Letter}' is a 'subst' standing in for '{real}'");
    }

    [Test]
    public async Task An83ShortNameIsCanonicalisedBeforeTheGuardEverSeesIt()
    {
        // ⚠️ **Measured 2026-08-19 on .NET 10, and it corrects the review this
        // work came from.** Finding A4 lists 8.3 short names among the things
        // `Path.GetFullPath` "does not resolve". On this toolchain it does:
        // `PathHelper.Normalize` expands a path containing `~` through the
        // filesystem, and it expands the part that EXISTS while leaving a
        // non-existent tail alone -- so a short spelling reaches this product
        // already canonical and there is no second identity to refuse.
        //
        // That is why this arm asserts an EQUALITY rather than a refusal. A
        // refusal here would be the wrong answer: the caller named the right
        // directory in a legal spelling and nothing about it is ambiguous by the
        // time the guard runs.
        using var scratch = ScratchDirectory.Create("guard-short-name");

        var real = Path.Combine(scratch.Path, "a real session directory");
        _ = Directory.CreateDirectory(real);

        var shortName = PathAliases.ShortNameOf(real);

        // The positive control. A volume with 8.3 generation switched off hands
        // back the long path, and every assertion below would then be asserting
        // that a path equals itself.
        await Assert.That(shortName).IsNotEqualTo(real);
        await Assert.That(shortName).Contains("~");

        await Assert.That(SessionPath.Resolve(shortName).Key).IsEqualTo(SessionPath.Resolve(real).Key);
        await Assert.That(SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(shortName))).IsNull();

        // And the half that matters for `init`: a short prefix with a tail that
        // does not exist yet is expanded too, tail preserved.
        var notYetThere = Path.Combine(shortName, "not", "created", "yet");

        await Assert.That(SessionPath.Resolve(notYetThere).FullPath).IsEqualTo(Path.Combine(real, "not", "created", "yet"));
        await Assert.That(SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(notYetThere))).IsNull();
    }

    [Test]
    public async Task NoSpellingOfOneDirectoryIsEverAdmittedUnderASecondIdentity()
    {
        // THE INVARIANT THE TWO ARMS ABOVE ARE HALVES OF, asserted over every
        // alias this machine can build. Whether a spelling is canonicalised into
        // the true path or refused at the door is an implementation detail split
        // across two layers; what may never happen is the third outcome -- a
        // spelling ADMITTED while hashing to a different key, which is two
        // mutexes over one lock.json and the whole point of the exercise.
        using var scratch = ScratchDirectory.Create("guard-invariant");

        var real = Path.Combine(scratch.Path, "one real session directory");

        // The session is a LEAF beneath the aliased directory rather than the
        // aliased directory itself, so that every form below -- including the
        // substituted drive, whose root SessionPath refuses as a volume root --
        // is a spelling of one and the same session.
        var session = Path.Combine(real, "the session");
        _ = Directory.CreateDirectory(session);

        var link = Path.Combine(scratch.Path, "invariant-junction");
        await PathAliases.JunctionAsync(link, real);

        using var substituted = DosDeviceAlias.Substituting(real);

        var identity = SessionPath.Resolve(session).Key;

        string[] spellings =
        [
            session,
            session + Path.DirectorySeparatorChar,
            session.ToUpperInvariant(),
            VolumeIdentity.ExtendedLengthPrefix + session,
            PathAliases.ShortNameOf(session),
            Path.Combine(link, "the session"),
            substituted.PathTo("the session"),
        ];

        foreach (var spelling in spellings)
        {
            var resolved = SessionPath.Resolve(spelling);
            var admitted = SessionDirectoryGuard.Refuse("directory", resolved) is null;

            await Assert.That(admitted && !string.Equals(resolved.Key, identity, StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task ADirectoryThatDoesNotExistYetIsJudgedByTheDeepestAncestorThatDoes()
    {
        // `init` names a directory nothing has created, which is the ordinary
        // case and the one a naive final-name check gets wrong: it cannot open
        // what is not there, and accepting on that basis would let every
        // aliasing parent through.
        using var scratch = ScratchDirectory.Create("guard-not-yet-there");

        var real = Path.Combine(scratch.Path, "parent");
        _ = Directory.CreateDirectory(real);

        var link = Path.Combine(scratch.Path, "parent-link");
        await PathAliases.JunctionAsync(link, real);

        // Three levels of directory that do not exist, under a junction.
        var underneath = Path.Combine(link, "not", "created", "yet");
        var refusal = SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(underneath));

        await Assert.That(refusal).IsNotNull();

        // The accepted spelling carries the tail back, so the next call is the
        // one the caller meant rather than the ancestor the guard happened to
        // resolve.
        await Assert.That(refusal!).Contains($"directory='{Path.Combine(real, "not", "created", "yet")}'");

        // The positive control for the walk itself: the same three missing
        // levels under an unaliased parent are accepted, so the assertion above
        // is about the junction rather than about the directories being absent.
        await Assert.That(SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(Path.Combine(real, "not", "created", "yet")))).IsNull();
    }

    [Test]
    public async Task CaseAndTrailingSeparatorsAreNotAliasesBecauseTheyAreOneSessionByDesign()
    {
        // The guard sits in front of the identity chain and must not contradict
        // it. SessionPathTests asserts that a case change and a trailing
        // separator are ONE session; a guard that refused them would make that
        // assertion unreachable through the tools.
        using var scratch = ScratchDirectory.Create("guard-case");

        var directory = Path.Combine(scratch.Path, "Mixed Case Session");
        _ = Directory.CreateDirectory(directory);

        string[] spellings =
        [
            directory,
            directory + Path.DirectorySeparatorChar,
            directory.ToUpperInvariant(),
            Path.Combine(scratch.Path, "mixed case session"),
            Path.Combine(scratch.Path, "elsewhere", "..", "Mixed Case Session"),
        ];

        foreach (var spelling in spellings)
        {
            await Assert.That(SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(spelling))).IsNull();
        }
    }

    private static async Task RefusedAsAnAlias(string spelling, string accepted)
    {
        var refusal = SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(spelling));

        await Assert.That(refusal).IsNotNull();

        // The accepted spelling, in the form the caller pastes back. This is the
        // half that makes the refusal recoverable in one turn, so it is asserted
        // as the literal argument rather than as a substring somewhere.
        await Assert.That(refusal!).Contains($"directory='{accepted}'");
    }
}

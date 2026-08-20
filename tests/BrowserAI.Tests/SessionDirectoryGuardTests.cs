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
/// <para>
/// ⚠️⚠️ <b>EVERY TEST BELOW THAT COMPOSES A PATH RUNS TWICE, ONCE PER
/// <see cref="DriveLetterCase"/>, AND THAT IS THE MECHANISM RATHER THAN
/// THOROUGHNESS.</b> Two of these tests were red from Git Bash and green from
/// PowerShell on the same commit, because the accepted spelling in a refusal is
/// read back from the filesystem — which always says <c>C:</c> — while the
/// expected string was composed from a root carrying whatever casing the
/// invoking shell handed the test host. <c>Sessions\CLAUDE.md</c> predicted this
/// exact defect and recorded that <i>nothing asserted it</i>; this is what now
/// does. The <see cref="DriveLetterCase.Lower"/> arm composes a spelling no
/// Windows API ever returns, so a comparison that is not case-insensitive fails
/// on <b>every</b> machine and in <b>every</b> shell — CI included, which runs
/// <c>pwsh</c> and could otherwise never see it. Added 2026-08-19, after the
/// second recurrence.
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
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task AMappedDriveLetterIsRefusedAsANetworkPathThoughItIsSpelledLocally(DriveLetterCase casing)
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

        await Assert.That(SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(Path.Combine(casing.Spell(scratch.Path), "session")))).IsNull();
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
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task EveryAliasPathGetFullPathLeavesAloneIsRefusedWithTheAcceptedSpelling(DriveLetterCase casing)
    {
        using var scratch = ScratchDirectory.Create("guard-aliases");

        // A name with a space in it, so this volume's 8.3 generator has
        // something to shorten.
        var real = Path.Combine(casing.Spell(scratch.Path), "a real session directory");
        _ = Directory.CreateDirectory(real);

        var canonical = SessionPath.Resolve(real).FullPath;

        // 1. The device-namespace prefix. Needs no filesystem setup at all,
        //    which is what makes it the one an unlucky caller reaches first --
        //    and `Path.GetFullPath` passes an extended-length path through
        //    untouched, so it arrives here exactly as the caller typed it.
        await RefusedAsAnAlias(VolumeIdentity.ExtendedLengthPrefix + real, canonical);

        // 2. A junction. mklink /J needs no privilege and no Developer Mode,
        //    where Directory.CreateSymbolicLink needs SeCreateSymbolicLink.
        var link = Path.Combine(casing.Spell(scratch.Path), "junction");
        await PathAliases.JunctionAsync(link, real);
        await RefusedAsAnAlias(link, canonical);

        // 3. A subst drive. Answered off the object manager without a single
        //    filesystem call, because the DOS device target IS the accepted
        //    spelling.
        using var substituted = DosDeviceAlias.Substituting(real);

        var refusal = SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(substituted.PathTo("page.png")));

        await Assert.That(refusal).IsNotNull();

        // Case-insensitive for the reason recorded in full at RefusedAsAnAlias:
        // the accepted spelling here comes back through QueryDosDeviceW rather
        // than from this process, so the two sides reach the comparison by
        // different routes and only one of them carries the shell's casing.
        await Assert.That(refusal!).Contains($"directory='{Path.Combine(real, "page.png")}'", StringComparison.OrdinalIgnoreCase);
        await Assert.That(refusal).Contains($"drive '{substituted.Letter}' is a 'subst' standing in for '{real}'", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task An83SpellingNeverReachesASecondIdentityOnEitherKindOfVolume(DriveLetterCase casing)
    {
        // ⚠️ **Measured 2026-08-19 on .NET 10, and it corrects the review this
        // work came from.** Finding A4 lists 8.3 short names among the things
        // `Path.GetFullPath` "does not resolve". On this toolchain it does:
        // `PathHelper.Normalize` expands a path containing `~` through the
        // filesystem, and it expands the part that EXISTS while leaving a
        // non-existent tail alone -- so a short spelling reaches this product
        // already canonical and there is no second identity to refuse. That is
        // why the first branch below asserts an EQUALITY rather than a refusal:
        // a refusal would be the wrong answer, because the caller named the
        // right directory in a legal spelling.
        //
        // ⚠️⚠️ **AND 8.3 GENERATION IS A PER-VOLUME SETTING, WHICH THIS TEST
        // FOUND OUT FROM CI RATHER THAN FROM A DOCUMENT.** The developer machine
        // generates short names on C:; the GitHub Windows runner does not on the
        // D: it checks out onto, so `GetShortPathNameW` hands back the long path
        // and there is no alias to build. **It is not skipped there**: a volume
        // with no 8.3 names is a volume on which this hazard does not exist, and
        // saying so is a real assertion. What the second branch proves instead is
        // the BACKSTOP -- that the guard refuses a path whose final name differs
        // -- which is the mechanism that would catch a short name if one ever
        // arrived unexpanded, and it is available on every volume.
        //
        // Which branch ran is printed rather than inferred, because a two-branch
        // test whose branch nobody can see is a test that can quietly take the
        // emptier one for ever.
        using var scratch = ScratchDirectory.Create("guard-short-name");

        var real = Path.Combine(casing.Spell(scratch.Path), "a real session directory");
        _ = Directory.CreateDirectory(real);

        var shortName = PathAliases.ShortNameOf(real);

        // Case-insensitive, and here it decides which BRANCH runs rather than
        // whether an assertion passes: GetShortPathNameW answers with the
        // filesystem's own spelling, so an ordinal compare against a path
        // composed in this process could read "unchanged" as "shortened" and
        // send this test down the arm that then asserts a tilde. See
        // DriveLetterCase and RefusedAsAnAlias.
        var volumeGeneratesShortNames = !string.Equals(shortName, real, StringComparison.OrdinalIgnoreCase);

        if (TestContext.Current?.OutputWriter is { } report)
        {
            await report.WriteLineAsync(
                volumeGeneratesShortNames
                    ? $"8.3 names ON for this volume: '{real}' is also '{shortName}'."
                    : $"8.3 names OFF for this volume: GetShortPathNameW returned '{real}' unchanged, so no short alias exists here and the backstop is asserted instead.");
        }

        if (volumeGeneratesShortNames)
        {
            await Assert.That(shortName).Contains("~");
            await Assert.That(SessionPath.Resolve(shortName).Key).IsEqualTo(SessionPath.Resolve(real).Key);
            await Assert.That(SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(shortName))).IsNull();

            // And the half that matters for `init`: a short prefix with a tail
            // that does not exist yet is expanded too, tail preserved.
            var notYetThere = Path.Combine(shortName, "not", "created", "yet");

            await Assert.That(SessionPath.Resolve(notYetThere).FullPath)
                .IsEqualTo(Path.Combine(real, "not", "created", "yet"), StringComparison.OrdinalIgnoreCase);
            await Assert.That(SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(notYetThere))).IsNull();

            return;
        }

        // No short alias on this volume, so the claim is that there is none --
        // asserted as a whole-string identity rather than as "it did not
        // contain a tilde", because a partially shortened path is the case a
        // tilde test would let through.
        await Assert.That(shortName).IsEqualTo(real, StringComparison.OrdinalIgnoreCase);

        // The backstop, on the same directory: whatever spelling reaches the
        // guard, a path the filesystem calls something else is refused with the
        // name it does call it. This is what would catch an 8.3 spelling on a
        // volume that had them and a .NET that had stopped expanding them.
        var link = Path.Combine(casing.Spell(scratch.Path), "short-name-backstop");
        await PathAliases.JunctionAsync(link, real);

        var refusal = SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(link));

        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!).Contains($"directory='{real}'", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task NoSpellingOfOneDirectoryIsEverAdmittedUnderASecondIdentity(DriveLetterCase casing)
    {
        // THE INVARIANT THE TWO ARMS ABOVE ARE HALVES OF, asserted over every
        // alias this machine can build. Whether a spelling is canonicalised into
        // the true path or refused at the door is an implementation detail split
        // across two layers; what may never happen is the third outcome -- a
        // spelling ADMITTED while hashing to a different key, which is two
        // mutexes over one browserai.json and the whole point of the exercise.
        using var scratch = ScratchDirectory.Create("guard-invariant");

        var real = Path.Combine(casing.Spell(scratch.Path), "one real session directory");

        // The session is a LEAF beneath the aliased directory rather than the
        // aliased directory itself, so that every form below -- including the
        // substituted drive, whose root SessionPath refuses as a volume root --
        // is a spelling of one and the same session.
        var session = Path.Combine(real, "the session");
        _ = Directory.CreateDirectory(session);

        var link = Path.Combine(casing.Spell(scratch.Path), "invariant-junction");
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
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task ADirectoryThatDoesNotExistYetIsJudgedByTheDeepestAncestorThatDoes(DriveLetterCase casing)
    {
        // `init` names a directory nothing has created, which is the ordinary
        // case and the one a naive final-name check gets wrong: it cannot open
        // what is not there, and accepting on that basis would let every
        // aliasing parent through.
        using var scratch = ScratchDirectory.Create("guard-not-yet-there");

        var real = Path.Combine(casing.Spell(scratch.Path), "parent");
        _ = Directory.CreateDirectory(real);

        var link = Path.Combine(casing.Spell(scratch.Path), "parent-link");
        await PathAliases.JunctionAsync(link, real);

        // Three levels of directory that do not exist, under a junction.
        var underneath = Path.Combine(link, "not", "created", "yet");
        var refusal = SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(underneath));

        await Assert.That(refusal).IsNotNull();

        // The accepted spelling carries the tail back, so the next call is the
        // one the caller meant rather than the ancestor the guard happened to
        // resolve. Case-insensitive: see RefusedAsAnAlias.
        await Assert.That(refusal!).Contains(
            $"directory='{Path.Combine(real, "not", "created", "yet")}'",
            StringComparison.OrdinalIgnoreCase);

        // The positive control for the walk itself: the same three missing
        // levels under an unaliased parent are accepted, so the assertion above
        // is about the junction rather than about the directories being absent.
        await Assert.That(SessionDirectoryGuard.Refuse("directory", SessionPath.Resolve(Path.Combine(real, "not", "created", "yet")))).IsNull();
    }

    [Test]
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task CaseAndTrailingSeparatorsAreNotAliasesBecauseTheyAreOneSessionByDesign(DriveLetterCase casing)
    {
        // The guard sits in front of the identity chain and must not contradict
        // it. SessionPathTests asserts that a case change and a trailing
        // separator are ONE session; a guard that refused them would make that
        // assertion unreachable through the tools.
        using var scratch = ScratchDirectory.Create("guard-case");

        var root = casing.Spell(scratch.Path);
        var directory = Path.Combine(root, "Mixed Case Session");
        _ = Directory.CreateDirectory(directory);

        string[] spellings =
        [
            directory,
            directory + Path.DirectorySeparatorChar,
            directory.ToUpperInvariant(),
            Path.Combine(root, "mixed case session"),
            Path.Combine(root, "elsewhere", "..", "Mixed Case Session"),
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
        //
        // ⚠️ CASE-INSENSITIVE, AND THAT IS THE CORRECT COMPARISON RATHER THAN A
        // LOOSENING — the third time this repository has had to say so, after
        // ErrorCatalogueTests and StraySweepTests on 2026-08-17. `accepted` is
        // composed in this process from a root carrying whatever drive-letter
        // case the invoking shell handed the test host; the spelling inside the
        // refusal was read back through GetFinalPathNameByHandleW, which always
        // reports it upper-case. Compared ordinally the same directory fails to
        // match itself from Git Bash and matches from PowerShell, which makes
        // the assertion a property of the caller. See DriveLetterCase.
        await Assert.That(refusal!).Contains($"directory='{accepted}'", StringComparison.OrdinalIgnoreCase);
    }
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The one path function: what it normalises, what it refuses, and what it
/// deliberately does not spend on a path it did not ask for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every alias below is real.</b> A junction comes from <c>mklink /J</c>, a
/// short name from <c>GetShortPathNameW</c>, and the <c>subst</c> and mapped
/// drives from <c>DefineDosDeviceW</c> — the same call <c>subst</c> and the
/// multiple-UNC provider make. None of the four needs administrator rights, so
/// none of them is skipped or asserted against a string the test wrote itself,
/// which is the failure mode a predicate like this invites: a predicate tested
/// only on inputs the test invented is a predicate tested against its own
/// author's idea of the alias.
/// </para>
/// <para>
/// ⚠️ <b>This file replaces <c>SessionDirectoryGuardTests</c>, and the inversion
/// is the point.</b> Until 2026-08-26 an aliased spelling was <i>refused</i> and
/// the refusal named the spelling to use instead; every alias that can be
/// resolved without touching a redirector is now <i>normalised</i>, and what is
/// refused is the network and the shapes Windows would silently rewrite. The
/// arms that asserted a refusal for <c>\\?\</c>, a junction and a <c>subst</c>
/// are therefore assertions about a canonical form here, against the same real
/// aliases, rather than deletions.
/// </para>
/// <para>
/// ⚠️ <b>What is asserted is which branch answered, never the clock.</b> The
/// failure these refusals prevent is a 22-second stall, so a stopwatch is the
/// obvious assertion and it is the wrong one twice over: it is a promptness
/// claim a loaded machine can fail while the product is correct, and no bound
/// with headroom a full-suite run cannot reach is also tight enough to catch one
/// 22-second stall. <see cref="TheNetworkRefusalDoesNotComeFromTheCallThatOpensThings"/>
/// says what its substitute can and cannot establish.
/// </para>
/// <para>
/// ⚠️⚠️ <b>EVERY TEST BELOW THAT COMPOSES A PATH RUNS TWICE, ONCE PER
/// <see cref="DriveLetterCase"/>, AND THAT IS THE MECHANISM RATHER THAN
/// THOROUGHNESS.</b> The canonical form is read back out of the filesystem,
/// which always answers <c>C:</c>, while a path composed in this process carries
/// whatever casing the invoking shell handed the test host. The
/// <see cref="DriveLetterCase.Lower"/> arm composes a spelling no Windows API
/// ever returns, so a comparison that is not case-insensitive fails on
/// <b>every</b> machine and in <b>every</b> shell.
/// </para>
/// </remarks>
internal sealed class CanonicalPathTests
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
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task EveryAliasPathGetFullPathLeavesAloneIsNormalisedToTheFilesystemsOwnSpelling(DriveLetterCase casing)
    {
        // The inversion, stated as three assertions about one directory. Each
        // spelling below was a refusal until 2026-08-26 and each carried the
        // accepted form INSIDE the refusal string -- so normalising them costs
        // no syscall that was not already being paid to compose that sentence.
        using var scratch = ScratchDirectory.Create("canonical-aliases");

        // A name with a space in it, so this volume's 8.3 generator has
        // something to shorten.
        var real = Path.Combine(casing.Spell(scratch.Path), "a real session directory");
        _ = Directory.CreateDirectory(real);

        var canonical = Named(real, "directory");

        // 1. The device-namespace prefix. Needs no filesystem setup at all,
        //    which is what makes it the one an unlucky caller reaches first --
        //    and `Path.GetFullPath` passes an extended-length path through
        //    untouched, so it arrives here exactly as the caller typed it.
        await NormalisesTo(VolumeIdentity.ExtendedLengthPrefix + real, canonical);

        // 2. A junction. mklink /J needs no privilege and no Developer Mode,
        //    where Directory.CreateSymbolicLink needs SeCreateSymbolicLink.
        var link = Path.Combine(casing.Spell(scratch.Path), "junction");
        await PathAliases.JunctionAsync(link, real);
        await NormalisesTo(link, canonical);

        // 3. A subst drive. Answered off the object manager without a single
        //    filesystem call, because the DOS device target IS the accepted
        //    spelling.
        using var substituted = DosDeviceAlias.Substituting(real);

        await NormalisesTo(substituted.PathTo("a session"), Path.Combine(canonical, "a session"));

        // And a leaf that does not exist yet under the junction, because that is
        // what `init` actually names: the tail goes back on rather than the
        // caller being sent to an ancestor.
        await NormalisesTo(Path.Combine(link, "not", "created", "yet"), Path.Combine(canonical, "not", "created", "yet"));
    }

    [Test]
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task EverySpellingOfOneDirectoryIsAdmittedUnderTheOneIdentity(DriveLetterCase casing)
    {
        // THE INVARIANT, and it is strictly stronger than the one this replaced.
        // Until 2026-08-26 it asserted only that no spelling was ADMITTED under a
        // second key -- which a refusal satisfies. Nothing is refused here any
        // more, so the claim is the whole one: every spelling is admitted, and
        // every one of them hashes to the same identity.
        using var scratch = ScratchDirectory.Create("canonical-invariant");

        var real = Path.Combine(casing.Spell(scratch.Path), "one real session directory");

        // The session is a LEAF beneath the aliased directory rather than the
        // aliased directory itself, so that every form below -- including the
        // substituted drive, whose root is a volume root -- is a spelling of one
        // and the same session.
        var session = Path.Combine(real, "the session");
        _ = Directory.CreateDirectory(session);

        var link = Path.Combine(casing.Spell(scratch.Path), "invariant-junction");
        await PathAliases.JunctionAsync(link, real);

        using var substituted = DosDeviceAlias.Substituting(real);

        var identity = SessionPath.For(Named(session, "directory")).Key;

        string[] spellings =
        [
            session,
            session + Path.DirectorySeparatorChar,
            session.ToUpperInvariant(),
            VolumeIdentity.ExtendedLengthPrefix + session,
            PathAliases.ShortNameOf(session),
            Path.Combine(link, "the session"),
            substituted.PathTo("the session"),
            Path.Combine(real, "elsewhere", "..", "the session"),
        ];

        foreach (var spelling in spellings)
        {
            var verdict = CanonicalPath.Of(spelling, PathOrigin.Named, "directory");

            await Assert.That(verdict.Refusal).IsNull();
            await Assert.That(SessionPath.For(verdict.Canonical!).Key).IsEqualTo(identity);
        }
    }

    [Test]
    public async Task ANetworkPathIsRefusedInEverySpellingItArrivesIn()
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
            var verdict = CanonicalPath.Of(spelling, PathOrigin.Named, "directory");

            await Assert.That(verdict.Canonical).IsNull();
            await Assert.That(verdict.Refusal!).Contains("is on a network path");

            // Named, so the caller can see which of several paths it was.
            await Assert.That(verdict.Refusal).Contains(spelling);
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

        var verdict = CanonicalPath.Of(mapped.PathTo("session"), PathOrigin.Named, "directory");

        await Assert.That(verdict.Canonical).IsNull();
        await Assert.That(verdict.Refusal!).Contains("is on a network path");

        // The clause is what makes this fixable: told only "that is a network
        // path" about a drive letter, a caller would reasonably think BrowserAI
        // had it wrong.
        await Assert.That(verdict.Refusal).Contains($"drive '{mapped.Letter}' is a mapped network drive");

        // And the positive control, without which the assertion above proves
        // only that SOMETHING was refused: the same shape on a local letter is
        // accepted.
        using var scratch = ScratchDirectory.Create("canonical-local-control");

        await Assert.That(CanonicalPath.Of(Path.Combine(casing.Spell(scratch.Path), "session"), PathOrigin.Named, "directory").Refusal).IsNull();
    }

    [Test]
    public async Task ASubstOntoAMappedDriveIsRefusedAsNetworkRatherThanSentBackForASecondTurn()
    {
        // ⚠️ The defect the design found by reading, closed by construction.
        // The old guard answered `subst` by building the target spelling and
        // refusing with it -- WITHOUT re-asking the volume question about the
        // target -- so `subst X: Z:\dir` over a mapped Z: named a path that
        // earns the NETWORK refusal on the very next turn. Two turns is exactly
        // what this catalogue is built to prevent.
        //
        // Normalising cannot have that shape: the substitution rewrites the path
        // and the whole sequence runs again, so the network question is asked of
        // the target.
        using var mapped = DosDeviceAlias.MappedTo(UnreachableShare);
        using var substituted = DosDeviceAlias.Substituting(mapped.PathTo("dir"));

        var verdict = CanonicalPath.Of(substituted.PathTo("session"), PathOrigin.Named, "directory");

        await Assert.That(verdict.Canonical).IsNull();
        await Assert.That(verdict.Refusal!).Contains("is on a network path");

        // The half that says it is one turn rather than two: nothing here offers
        // a spelling to call back with, because there is no local one.
        await Assert.That(verdict.Refusal).DoesNotContain("Call the same tool again with");
    }

    [Test]
    public async Task TheNetworkRefusalDoesNotComeFromTheCallThatOpensThings()
    {
        // ⚠️ **This is the arm a stopwatch would have been written for, and the
        // stopwatch was deliberately not written.** What wants proving is that
        // the sequence answers the network question BEFORE the one call in it
        // that opens a directory -- and elapsed time is the only witness to an
        // ordering, which makes it a promptness assertion a starved machine can
        // fail while the product is correct. TESTING.md's rule is that a
        // duration is a hang detector taken from TestDefaults or it is a defect,
        // and no hang detector with headroom a full-suite run cannot reach is
        // also tight enough to catch a single 22-second stall.
        //
        // So what is asserted instead is WHICH BRANCH ANSWERED, decisively:
        // through a mapped drive the opening call cannot produce an answer at
        // all -- it returns null, which the walk treats as "accepted,
        // unverified" -- so a refusal proves the object-manager branch is what
        // produced it.
        //
        // What this still cannot say is that the open did not ALSO happen, 22
        // seconds before the right answer arrived. That gap is real and is named
        // here rather than papered over; what stands against it is
        // VolumeIdentity's own rule that FinalNameOf is never called on a path
        // Of has not already found local, and the kb row that measures why.
        using var mapped = DosDeviceAlias.MappedTo(UnreachableShare);

        var spelling = mapped.PathTo("session");

        await Assert.That(VolumeIdentity.Of(spelling).Kind).IsEqualTo(VolumeKind.Network);
        await Assert.That(VolumeIdentity.FinalNameOf(spelling)).IsNull();

        var verdict = CanonicalPath.Of(spelling, PathOrigin.Named, "directory");

        await Assert.That(verdict.Refusal!).Contains("is on a network path");

        // The control for the middle assertion: FinalNameOf really does answer
        // for a directory it can open, so "it returned null" above is about the
        // share rather than about the call being broken.
        using var scratch = ScratchDirectory.Create("canonical-final-name-control");

        await Assert.That(VolumeIdentity.FinalNameOf(scratch.Path)).IsNotNull();
    }

    [Test]
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task TheDeviceNamespaceIsRefusedWhileTheExtendedPrefixIsNormalised(DriveLetterCase casing)
    {
        // ⚠️ THE TWO PREFIXES ARE NOT ONE THING, and this is the only place in
        // the product that says so about a directory. `\\?\` is a length-and-
        // parsing prefix over an ordinary path, so stripping it is free and
        // loses nothing. `\\.\` is the DEVICE NAMESPACE, where `\\.\NUL` and
        // `\\.\PhysicalDrive0` name devices rather than directories -- and the
        // deleted filename gate refused it for exactly that reason, in those
        // words. Making the directory rule agree with the filename rule removes
        // an asymmetry rather than adding a rule.
        using var scratch = ScratchDirectory.Create("canonical-device-namespace");

        var real = Path.Combine(casing.Spell(scratch.Path), "device-namespace");
        _ = Directory.CreateDirectory(real);

        var refused = CanonicalPath.Of(@"\\.\" + real, PathOrigin.Named, "directory");

        await Assert.That(refused.Canonical).IsNull();
        await Assert.That(refused.Refusal!).Contains("the device namespace");

        // One turn, by construction: the accepted form is the same string minus
        // four characters, so the next call is this call with one argument
        // replaced.
        await Assert.That(refused.Refusal).Contains($"directory='{real}'", StringComparison.OrdinalIgnoreCase);

        // And the pair that makes the distinction a distinction rather than an
        // inconsistency: the extended prefix over the same directory is not
        // refused at all.
        await Assert.That(CanonicalPath.Of(VolumeIdentity.ExtendedLengthPrefix + real, PathOrigin.Named, "directory").Refusal).IsNull();
    }

    [Test]
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task ANameWindowsWillNotKeepVerbatimIsRefusedBeforeAnythingCanSilentlyRewriteIt(DriveLetterCase casing)
    {
        // ⚠️ THE ORDER IS THE ASSERTION. Every shape below is refused on the
        // string BEFORE `Path.GetFullPath` is called, because GetFullPath does
        // not reject them -- it REWRITES them, and a directory that comes back
        // under a different name than the caller asked for is the two-spellings
        // failure arriving by the other door.
        //
        // Measured 2026-08-26 on .NET 10.0.11, Windows 11 Pro 26200, and
        // asserted here rather than quoted, so a runtime that stopped doing it
        // shows up as a red test rather than as a stale comment:
        using var scratch = ScratchDirectory.Create("canonical-hostile-names");

        var root = casing.Spell(scratch.Path);

        // The positive controls first: this is what the product is protecting
        // against, produced by the framework rather than described.
        await Assert.That(Path.GetFullPath(Path.Combine(root, "sess."))).IsEqualTo(Path.Combine(root, "sess"), StringComparison.OrdinalIgnoreCase);
        await Assert.That(Path.GetFullPath(Path.Combine(root, "sess "))).IsEqualTo(Path.Combine(root, "sess"), StringComparison.OrdinalIgnoreCase);
        await Assert.That(Path.GetFullPath(Path.Combine(root, "NUL"))).IsEqualTo(@"\\.\NUL");

        (string Spelling, string Because)[] hostile =
        [
            (Path.Combine(root, "sess."), "ends with"),
            (Path.Combine(root, "sess "), "ends with"),
            (Path.Combine(root, "NUL"), "reserved device name"),
            (Path.Combine(root, "NUL.png"), "reserved device name"),
            (Path.Combine(root, "sess:stream"), "alternate data stream"),
            (Path.Combine(root, "sess*"), "cannot appear"),
        ];

        foreach (var (spelling, because) in hostile)
        {
            var verdict = CanonicalPath.Of(spelling, PathOrigin.Named, "directory");

            await Assert.That(verdict.Canonical).IsNull();
            await Assert.That(verdict.Refusal!).Contains(because);
        }

        // And the control that says the refusals above are about the NAMES: a
        // dot inside a segment, a `..` segment and a space inside a segment are
        // all ordinary and all accepted.
        await Assert.That(CanonicalPath.Of(Path.Combine(root, "sess.one", "..", "sess two"), PathOrigin.Named, "directory").Refusal).IsNull();
    }

    [Test]
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task AReadNeverAsksAQuestionThatCostsASyscallAndRefusesWhatThisBuildWouldNotHaveWritten(DriveLetterCase casing)
    {
        // ⚠️ **`Read` is asserted by CONSTRUCTION, not by a clock**, and this is
        // the decisive half: a mapped network drive is the one shape whose
        // answer requires the object manager, so a `Read` that ACCEPTS one has
        // provably not asked. An implementation that leaked the volume question
        // into `Read` refuses here, and this arm is red.
        using var mapped = DosDeviceAlias.MappedTo(UnreachableShare);

        await Assert.That(CanonicalPath.Of(mapped.PathTo("session"), PathOrigin.Read, "session").Refusal).IsNull();

        // The same string under `Named` is refused, which is what makes the line
        // above about the ORIGIN rather than about the mapping being invisible.
        await Assert.That(CanonicalPath.Of(mapped.PathTo("session"), PathOrigin.Named, "session").Refusal).IsNotNull();

        using var scratch = ScratchDirectory.Create("canonical-read");

        var root = casing.Spell(scratch.Path);
        var real = Path.Combine(root, "recorded");
        _ = Directory.CreateDirectory(real);

        // What a stored path may be: the spelling this build writes, and that
        // spelling with a separator a model appended. Nothing else.
        await Assert.That(CanonicalPath.Of(real, PathOrigin.Read, "session").Canonical).IsEqualTo(real);
        await Assert.That(CanonicalPath.Of(real + Path.DirectorySeparatorChar, PathOrigin.Read, "session").Canonical).IsEqualTo(real);

        // ⚠️ A TILDE IS NOT REFUSED, and the design this was built from said it
        // should be. Refusing one would make `Read` provably free of the 8.3
        // expansion `Path.GetFullPath` performs -- but `Read` never calls
        // GetFullPath at all, so the justification is circular, and a directory
        // a person genuinely named `my~project` would become unfindable for the
        // life of the session. The 8.3 spelling this would have caught cannot be
        // in a record this build wrote, because the writer resolved it.
        var tilde = Path.Combine(root, "my~project");
        _ = Directory.CreateDirectory(tilde);

        await Assert.That(CanonicalPath.Of(tilde, PathOrigin.Read, "session").Canonical).IsEqualTo(tilde);

        string[] notCanonical =
        [
            VolumeIdentity.ExtendedLengthPrefix + real,
            Path.Combine(root, "elsewhere", "..", "recorded"),
            real.Replace('\\', '/'),
            @"\\10.255.255.1\share\session",
            @"relative\path",
            Path.Combine(root, "sess."),
        ];

        foreach (var spelling in notCanonical)
        {
            var verdict = CanonicalPath.Of(spelling, PathOrigin.Read, "session");

            await Assert.That(verdict.Canonical).IsNull();
            await Assert.That(verdict.Refusal).IsNotNull();
        }
    }

    [Test]
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task An83SpellingNeverReachesASecondIdentityOnEitherKindOfVolume(DriveLetterCase casing)
    {
        // ⚠️ **Measured 2026-08-19 on .NET 10.** Finding A4 lists 8.3 short
        // names among the things `Path.GetFullPath` "does not resolve". On this
        // toolchain it does: `PathHelper.Normalize` expands a path containing
        // `~` through the filesystem, and it expands the part that EXISTS while
        // leaving a non-existent tail alone -- so a short spelling arrives
        // already canonical.
        //
        // ⚠️⚠️ **AND 8.3 GENERATION IS A PER-VOLUME SETTING, WHICH THIS TEST
        // FOUND OUT FROM CI RATHER THAN FROM A DOCUMENT.** A volume with no 8.3
        // names is a volume on which this hazard does not exist, and saying so
        // is a real assertion -- so the second branch proves the BACKSTOP
        // instead, which is available on every volume: a path the filesystem
        // calls something else is answered with the name it does call it.
        //
        // Which branch ran is printed rather than inferred, because a two-branch
        // test whose branch nobody can see is a test that can quietly take the
        // emptier one for ever.
        using var scratch = ScratchDirectory.Create("canonical-short-name");

        var real = Path.Combine(casing.Spell(scratch.Path), "a real session directory");
        _ = Directory.CreateDirectory(real);

        var canonical = Named(real, "directory");
        var shortName = PathAliases.ShortNameOf(real);
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
            await NormalisesTo(shortName, canonical);
            await NormalisesTo(Path.Combine(shortName, "not", "created", "yet"), Path.Combine(canonical, "not", "created", "yet"));

            return;
        }

        // No short alias on this volume, so the claim is that there is none --
        // asserted as a whole-string identity rather than as "it did not
        // contain a tilde", because a partially shortened path is the case a
        // tilde test would let through.
        await Assert.That(shortName).IsEqualTo(real, StringComparison.OrdinalIgnoreCase);

        var link = Path.Combine(casing.Spell(scratch.Path), "short-name-backstop");
        await PathAliases.JunctionAsync(link, real);
        await NormalisesTo(link, canonical);
    }

    [Test]
    [Arguments(DriveLetterCase.Upper)]
    [Arguments(DriveLetterCase.Lower)]
    public async Task CaseAndTrailingSeparatorsAreNotAliasesBecauseTheyAreOneSessionByDesign(DriveLetterCase casing)
    {
        // The function sits in front of the identity chain and must not
        // contradict it. SessionPathTests asserts that a case change and a
        // trailing separator are ONE session; refusing them would make that
        // assertion unreachable through the tools.
        using var scratch = ScratchDirectory.Create("canonical-case");

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
            var verdict = CanonicalPath.Of(spelling, PathOrigin.Named, "directory");

            await Assert.That(verdict.Refusal).IsNull();

            // The filesystem's own spelling, which is the one the directory was
            // created under -- so every one of the five above lands on it.
            await Assert.That(verdict.Canonical).IsEqualTo(directory, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Test]
    public async Task AVolumeRootIsCanonicalForAListingAndStillNotASessionDirectory()
    {
        // ⚠️ THE ONE SHAPE WHOSE ANSWER DEPENDS ON THE CALLER, and the reason
        // the function and the identity are two types. `browserai_list` is
        // pointed at a volume root to see everything on it; `browserai_init`
        // taking a whole volume as a session directory is the accident that
        // rule exists to stop. So the canonicaliser accepts it and
        // `SessionPath.For` is what refuses it -- which is what let `list` stop
        // being the one caller with a path chain of its own.
        var verdict = CanonicalPath.Of(@"C:\", PathOrigin.Named, "directory");

        await Assert.That(verdict.Refusal).IsNull();

        // `C:` and `C:\` are different things and only one of them is a volume
        // root: `C:` is drive-relative and means "the current directory on C",
        // so a trim that produced it would be a silently different answer.
        await Assert.That(verdict.Canonical).IsEqualTo(@"C:\");

        _ = Assert.Throws<ArgumentException>(() => _ = SessionPath.For(verdict.Canonical!));
    }

    [Test]
    public async Task ThePrefixIsDerivedInExactlyOnePlaceAndCoversAWholeVolume()
    {
        // W8. `SessionManager.Subtree` and `SessionManager.Beneath` derived this
        // separately -- upper-case, then append a separator -- while
        // `SessionIndex.IsUnder`'s own remark forbade re-deriving the predicate
        // in as many words. One derivation, and a tree-as-text scan in
        // HouseRuleTests is what keeps it one.
        await Assert.That(CanonicalPath.PrefixOf(@"C:\work")).IsEqualTo(@"C:\WORK\");
        await Assert.That(CanonicalPath.PrefixOf(@"C:\work\")).IsEqualTo(@"C:\WORK\");

        // The volume root, which is the case a re-derivation gets wrong: `C:\`
        // upper-cased already ends with a separator, so appending a second one
        // produces a prefix nothing is ever under.
        await Assert.That(CanonicalPath.PrefixOf(@"C:\")).IsEqualTo(@"C:\");
    }

    /// <summary>
    /// <c>PathVerdict.Unestablished</c> is reachable by DEPTH alone, and the
    /// walk limit is exactly where it starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the control the third verdict has never had, and the record
    /// said it could not exist.</b> P5's could-not-check read <i>"Unestablished
    /// unexercised (honest — unreachable without <c>Create</c> failing too)"</i>.
    /// That is false: more non-existent levels than
    /// <c>CanonicalPath.AncestorWalkLimit</c> exhausts the walk, <c>final</c>
    /// comes back <see langword="null"/>, and the path is served with the
    /// caller's own spelling — while .NET creates the tree happily, so the
    /// session opens. Measured 2026-08-26 through the published binary with a
    /// clean bisect: 60 levels, no note; 66 levels, the note <b>and</b> a session
    /// that opened and was then destroyed. No ACL, no denied ancestor and no
    /// exotic machine is needed.
    /// </para>
    /// <para>
    /// <b>Both directions, at the boundary, because either one alone is
    /// satisfiable by the wrong thing.</b> A walk that had stopped answering at
    /// all would set the note on the shallow path too; a walk that never gave up
    /// would set it on neither.
    /// </para>
    /// <para>
    /// <b>The note quotes the ancestor the walk gave up on AND the caller's own
    /// path, and that pairing is asserted rather than left to read well.</b> The
    /// ancestor on its own is an intermediate path that means nothing to a
    /// caller; the caller's own path on its own does not say how far the walk
    /// got. It was considered as a one-or-the-other and kept as both.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheAncestorWalkGivesUpExactlyPastItsLimitAndSaysSoWhileStillAnsweringAPath()
    {
        using var scratch = ScratchDirectory.Create("walk-limit");

        // Composed from the product's own bound. A number written here would be
        // a second copy of it, and the arm would stop testing the boundary the
        // day somebody moved the constant.
        var inside = Compose(scratch.Path, CanonicalPath.AncestorWalkLimit - 2);
        var past = Compose(scratch.Path, CanonicalPath.AncestorWalkLimit + 2);

        var shallow = CanonicalPath.Of(inside, PathOrigin.Named, "directory");

        await Assert.That(shallow.Refusal).IsNull();
        await Assert.That(shallow.Unestablished).IsNull();
        await Assert.That(shallow.Canonical).IsEqualTo(inside, StringComparison.OrdinalIgnoreCase);

        var deep = CanonicalPath.Of(past, PathOrigin.Named, "directory");

        // Not a refusal. The path is served with the caller's own spelling and
        // the reason it could not be verified travels beside it -- which is the
        // whole of what the third outcome is for.
        await Assert.That(deep.Refusal).IsNull();
        await Assert.That(deep.Canonical).IsEqualTo(past, StringComparison.OrdinalIgnoreCase);
        await Assert.That(deep.Unestablished).IsNotNull();
        await Assert.That(deep.Unestablished!).Contains("would not say what it calls");
        await Assert.That(deep.Unestablished!).Contains(past, StringComparison.OrdinalIgnoreCase);

        // The ancestor the walk gave up on, derived rather than guessed: it
        // climbed exactly AncestorWalkLimit levels from a path two deeper, so it
        // stopped two levels above the root.
        await Assert.That(deep.Unestablished!)
            .Contains(Compose(scratch.Path, CanonicalPath.AncestorWalkLimit + 2 - CanonicalPath.AncestorWalkLimit), StringComparison.OrdinalIgnoreCase);

        // And the note reaches a caller rather than stopping at the verdict:
        // `SessionManager.SpellingNote` is what puts it in an `init` answer, and
        // that path is exercised end to end by SessionToolTests.
        await Assert.That(SessionPath.For(deep.Canonical!).FullPath).IsEqualTo(past, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A path with a given number of levels that do not exist.</summary>
    /// <param name="root">An existing directory.</param>
    /// <param name="levels">How many non-existent levels to add.</param>
    /// <returns>The composed path.</returns>
    private static string Compose(string root, int levels)
    {
        var path = root;

        for (var level = 0; level < levels; level++)
        {
            path = Path.Combine(path, "L");
        }

        return path;
    }

    private static string Named(string spelling, string argument)
    {
        var verdict = CanonicalPath.Of(spelling, PathOrigin.Named, argument);

        return verdict.Canonical
            ?? throw new InvalidOperationException($"'{spelling}' was refused, and this test needs its canonical form: {verdict.Refusal}");
    }

    private static async Task NormalisesTo(string spelling, string canonical)
    {
        var verdict = CanonicalPath.Of(spelling, PathOrigin.Named, "directory");

        await Assert.That(verdict.Refusal).IsNull();

        // ⚠️ CASE-INSENSITIVE, AND THAT IS THE CORRECT COMPARISON RATHER THAN A
        // LOOSENING. The canonical form is read back through
        // GetFinalPathNameByHandleW, which always reports the drive letter
        // upper-case; `canonical` is composed in this process from a root
        // carrying whatever case the invoking shell handed the test host.
        // Compared ordinally the same directory fails to match itself from Git
        // Bash and matches from PowerShell, which makes the assertion a property
        // of the caller. See DriveLetterCase.
        await Assert.That(verdict.Canonical!).IsEqualTo(canonical, StringComparison.OrdinalIgnoreCase);

        // And the claim the string comparison is standing in for: one identity.
        await Assert.That(SessionPath.For(verdict.Canonical!).Key).IsEqualTo(SessionPath.For(canonical).Key);
    }
}

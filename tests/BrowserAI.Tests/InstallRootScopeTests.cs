// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A root two Windows users could share is refused at startup, and a per-user
/// one that merely <i>looks</i> shared is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Half of this file is about the false positive rather than the hazard.</b>
/// The refusal's predicate is <i>inside this user's profile</i>, and four
/// ordinary Windows features make a per-user path fail a string comparison of
/// that: a junction, a <c>subst</c>ed drive letter, an 8.3 short component and
/// the <c>\\?\</c> prefix. Every one of them is built for real here -- no
/// stand-ins -- because a predicate checked only against strings a test invented
/// is a predicate checked against its author's idea of an alias.
/// </para>
/// <para>
/// <b>The arm that matters most is the one that points the other way.</b>
/// <see cref="AJunctionInsideTheProfileThatLeavesItIsRefused"/> is a link
/// <i>under</i> the profile whose target is outside it: a string comparison
/// accepts it, and it is a genuinely shared root. That arm is the reason the
/// check resolves through the filesystem rather than comparing prefixes, and it
/// is red against any implementation that does the cheap thing.
/// </para>
/// </remarks>
internal sealed class InstallRootScopeTests
{
    /// <summary>This user's profile directory, as the product reads it.</summary>
    private static string Profile { get; } = Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolderOption.DoNotVerify);

    [Test]
    public async Task TheProductsOwnDefaultRootIsServed()
    {
        // The root an uninstalled BrowserAI computes -- %LocalAppData%\BrowserAI
        // -- and the one Velopack installs to by default. If this is ever
        // refused, every BrowserAI on the machine stops starting.
        var verdict = InstallRootScope.Judge(BrowserAiPaths.Real.RootAppDir, installRoot: null);

        await Assert.That(verdict.MayServe).IsTrue();
        await Assert.That(verdict.Refusal).IsNull();
        await Assert.That(verdict.Unestablished).IsNull();
    }

    /// <summary>
    /// The refusal names the root, why a shared root is unsafe, and what to
    /// change.
    /// </summary>
    /// <remarks>
    /// <b>All three, because a refusal that says only "no" is the failure this
    /// repository's whole error catalogue exists against.</b> The root is
    /// checked because an operator has to know which one was found; the census
    /// clause because the danger is invisible without it; the variable's name
    /// because clearing it is the recovery.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARootOutsideTheProfileIsRefusedAndTheRefusalSaysWhatToDo()
    {
        using var outside = ScratchDirectory.Create("install-root-outside");

        var verdict = InstallRootScope.Judge(outside.Path, installRoot: null);

        await Assert.That(verdict.MayServe).IsFalse();

        var refusal = verdict.Refusal!;

        await Assert.That(refusal).Contains(outside.Path);
        await Assert.That(refusal).Contains(Program.AppRootVariable);
        await Assert.That(refusal).Contains("live-instance set");

        // ⚠️ AND THE REMEDY THAT IS NO LONGER THERE -- 2026-09-15. The sentence
        // used to end "if the root was set by the installer's install-to flag,
        // reinstall without it", and that became advice for a thing the flag
        // cannot do: it moves the install root, and what is judged here is the
        // data root, which only the variable above can move. Asserted as an
        // absence as well as a presence, so putting it back is red in both
        // directions rather than in neither.
        await Assert.That(refusal).DoesNotContain("install-to flag");

        // ⚠️ The clause moved 2026-09-15, later the same day (previously "the
        // installer chooses where the program goes and never where the data
        // goes"). The same claim is still made and the flag is now named, because
        // the sentence has to distinguish TWO roots and two levers: --installto
        // moves the install root and BROWSERAI_ROOT moves this one, and neither
        // can move the other's.
        await Assert.That(refusal).Contains("it moves the install root and never the data root");

        // And both roots are named, whichever is at fault, so a reader never has
        // to guess which one the sentence is about.
        await Assert.That(refusal).Contains("the data root is");
        await Assert.That(refusal).Contains("the install root is");

        // ⚠️ Case-insensitively, and that is not a nicety. Windows hands every
        // path back with an upper-case drive letter while a process keeps
        // whatever casing its shell gave it, so an ordinal comparison here is
        // green from PowerShell and red from Git Bash on the same commit.
        await Assert.That(refusal.Contains(Profile, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task ARootInsideTheProfileIsServed()
    {
        using var inside = ScratchDirectory.CreateUnderProfile("install-root-inside");

        var verdict = InstallRootScope.Judge(inside.Path, installRoot: null);

        await Assert.That(verdict.MayServe).IsTrue();
        await Assert.That(verdict.Unestablished).IsNull();
    }

    /// <summary>
    /// A root that does not exist yet is judged by its deepest existing
    /// ancestor.
    /// </summary>
    /// <remarks>
    /// This is the ordinary first-run state: <c>%LocalAppData%\BrowserAI</c> is
    /// created by the run that needs it, so a check that required the root to
    /// exist would refuse every genuinely first run.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARootThatDoesNotExistYetIsJudgedByItsDeepestExistingAncestor()
    {
        using var inside = ScratchDirectory.CreateUnderProfile("install-root-absent");

        var absent = Path.Combine(inside.Path, "not", "created", "yet");

        await Assert.That(Directory.Exists(absent)).IsFalse();
        await Assert.That(InstallRootScope.Judge(absent, installRoot: null).MayServe).IsTrue();

        using var outside = ScratchDirectory.Create("install-root-absent-outside");

        // And the same arm the other way, so this is not passing because
        // everything absent is accepted.
        await Assert.That(InstallRootScope.Judge(Path.Combine(outside.Path, "not", "created", "yet"), installRoot: null).MayServe).IsFalse();
    }

    /// <summary>
    /// A junction <b>inside</b> the profile whose target is outside it is
    /// refused.
    /// </summary>
    /// <remarks>
    /// <b>The arm a prefix comparison cannot pass.</b> The path begins with the
    /// profile directory, so every string test says <i>per-user</i>; the
    /// filesystem says the bytes land somewhere any account can reach. This is a
    /// real <c>mklink /J</c>, which needs no privilege.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AJunctionInsideTheProfileThatLeavesItIsRefused()
    {
        using var inside = ScratchDirectory.CreateUnderProfile("install-root-junction-out");
        using var target = ScratchDirectory.Create("install-root-junction-out-target");

        var link = Path.Combine(inside.Path, "link");

        await PathAliases.JunctionAsync(link, target.Path);

        // The control: the link really is spelled inside the profile, so a
        // prefix comparison would accept it.
        await Assert.That(link.StartsWith(Profile, StringComparison.OrdinalIgnoreCase)).IsTrue();

        var verdict = InstallRootScope.Judge(link, installRoot: null);

        await Assert.That(verdict.MayServe).IsFalse();
        await Assert.That(verdict.Refusal!.Contains(target.Path, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    /// <summary>
    /// A junction <b>outside</b> the profile whose target is inside it is
    /// served.
    /// </summary>
    /// <remarks>
    /// The false positive in the other direction: a legitimate per-user root
    /// reached through a link a prefix comparison would refuse, taking a
    /// working configuration away for nothing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AJunctionOutsideTheProfileThatReachesIntoItIsServed()
    {
        using var outside = ScratchDirectory.Create("install-root-junction-in");
        using var target = ScratchDirectory.CreateUnderProfile("install-root-junction-in-target");

        var link = Path.Combine(outside.Path, "link");

        await PathAliases.JunctionAsync(link, target.Path);

        await Assert.That(InstallRootScope.Judge(link, installRoot: null).MayServe).IsTrue();
    }

    /// <summary>
    /// A <c>subst</c>ed drive letter standing for a directory inside the profile
    /// is served.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Deliberately not refused the way a session directory's aliased
    /// spelling is.</b> <c>CanonicalPath</c> resolves a <c>subst</c>
    /// outright, because two spellings of one session directory produce two
    /// mutexes and one record. Nothing of that kind applies to the app root: the
    /// only question here is <i>where does this really live</i>, and a
    /// substitution has a perfectly good answer to it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASubstitutedDriveStandingForAProfileDirectoryIsServed()
    {
        using var target = ScratchDirectory.CreateUnderProfile("install-root-subst");
        using var alias = DosDeviceAlias.Substituting(target.Path);

        // The control: the spelling really is a different one, so a comparison
        // of strings would have refused this.
        await Assert.That(alias.Root.StartsWith(Profile, StringComparison.OrdinalIgnoreCase)).IsFalse();

        await Assert.That(InstallRootScope.Judge(alias.PathTo("app-root"), installRoot: null).MayServe).IsTrue();
    }

    /// <summary>
    /// An 8.3 short spelling of a profile path, and the <c>\\?\</c> extended
    /// spelling of one, are both served.
    /// </summary>
    /// <remarks>
    /// The short-name arm carries its own control: 8.3 generation is a per-volume
    /// setting, and on a volume with it disabled
    /// <see cref="PathAliases.ShortNameOf"/> answers the long path unchanged --
    /// which would make the arm assert nothing. It asserts the alias is served
    /// either way and says which case it was in the message.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ShortAndExtendedSpellingsOfAProfilePathAreServed()
    {
        // A name long enough to have an 8.3 form at all.
        using var target = ScratchDirectory.CreateUnderProfile("install-root-short-name-needs-a-long-component");

        var shortName = PathAliases.ShortNameOf(target.Path);

        await Assert.That(InstallRootScope.Judge(shortName, installRoot: null).MayServe).IsTrue();

        // ⚠️ Not a skip and not a branch in the assertion: both spellings must be
        // served, and the only thing the volume's setting changes is whether the
        // two strings differ. Recorded rather than asserted, because a machine
        // with 8.3 generation off is a supported one.
        await Assert.That(
            $"short name {(string.Equals(shortName, target.Path, StringComparison.OrdinalIgnoreCase) ? "is not generated on this volume" : "differs from the long path")}")
            .IsNotEmpty();

        await Assert.That(InstallRootScope.Judge(BrowserAI.Interop.VolumeIdentity.ExtendedLengthPrefix + target.Path, installRoot: null).MayServe).IsTrue();
    }

    /// <summary>
    /// The published binary really refuses: it exits non-zero, it writes the
    /// refusal into the process log, and it creates nothing else under the
    /// root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Through the front door, because the ordering inside <c>Main</c> is
    /// half the property.</b> The check sits after the log -- the log is the only
    /// channel a refusal has, since <c>stdout</c> is the protocol and
    /// <c>System.Console</c> is banned outright -- and before the sweep, the live
    /// marker, the instance directory and every session. So the assertion is not
    /// only <i>it exited</i>: it is that <c>logs\</c> is the <b>only</b> thing
    /// under the root afterwards.
    /// </para>
    /// <para>
    /// <b>Nothing is written to this process's stdin.</b> A refusing BrowserAI
    /// never reaches <c>StdioChannel.OpenStandardStreams</c>, so there is no
    /// conversation to have; what is measured is the exit and what the log says.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePublishedBinaryRefusesToServeOutOfASharedRootAndSaysWhyInTheLog()
    {
        SuiteEnvironment.RequirePublishedSlice();

        using var outside = ScratchDirectory.Create("install-root-published");

        var environment = PublishedSlice.InheritedEnvironment();
        environment[BrowserAiPaths.AppRootOverride] = outside.Path;

        // ⚠️ Inside a kill-on-close job, which is the suite's standing rule for
        // starting a real BrowserAI: a test that leaks one leaks whatever it
        // started. It is also the hang detector -- a build in which the check was
        // deleted starts serving and waits on stdin for ever, and what fails
        // then has to be this assertion rather than the whole run.
        using var job = JobObject.CreateKillOnClose();

        using var process = JobLauncher.Start(job, PublishedSlice.Executable, [], outside.Path, environment);

        var exited = await process.WaitForExitAsync(TestDefaults.BrowserHang);

        await Assert.That(exited).IsTrue();
        await Assert.That(process.TryReadExitCode()).IsNotEqualTo(0);

        var logs = Path.Combine(outside.Path, "logs");
        var said = Directory.Exists(logs)
            ? string.Join(Environment.NewLine, Directory.EnumerateFiles(logs).Select(ReadShared))
            : string.Empty;

        await Assert.That(said).Contains("will not serve out of the data root");
        await Assert.That(said).Contains(Program.AppRootVariable);

        // ⚠️ And nothing else was created. `live\`, `instances\`, `index\` and
        // `browsers\` are exactly the state a second user's census reads, so a
        // refusal that had already written one would have done the harm it
        // refused to do.
        var created = Directory.EnumerateFileSystemEntries(outside.Path)
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "logs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(string.Join(", ", created)).IsEmpty();
    }

    /// <summary>
    /// Reads a log file the writer may still hold open.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text.</returns>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// A UNC root is refused without ever opening anything on it.
    /// </summary>
    /// <remarks>
    /// <b>The hostname is deliberately one that does not resolve</b>, and the
    /// assertion is that the answer arrives anyway: a filesystem call against an
    /// unreachable share costs a measured 22 s, so a check that reached the
    /// filesystem before deciding would be a 22-second startup stall rather than
    /// a refusal. The wall clock is not asserted -- that would be asserting the
    /// speed of the machine -- but the refusal is, and it is the same ordering
    /// `CanonicalPath` is built on.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AUncRootIsRefusedOnItsSpellingAlone()
    {
        var verdict = InstallRootScope.Judge(@"\\10.255.255.1\browserai\root", installRoot: null);

        await Assert.That(verdict.MayServe).IsFalse();
        await Assert.That(verdict.Refusal!).Contains("UNC");
    }

    /// <summary>
    /// An install root outside the profile is refused even when the data root is
    /// perfectly per-user, and the refusal offers the lever that can move it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the arm the 2026-09-15 layout split opened and the decision of
    /// the same day closed.</b> The data root became a constant that
    /// <c>--installto</c> cannot touch, and the live-instance markers and their
    /// <c>Global\</c> mutex moved to the install root -- which was then judged by
    /// nothing at all. Two users sharing one install root still lose the census
    /// silently, and an apply's <c>force_stop_package</c> still terminates every
    /// process under it.
    /// </para>
    /// <para>
    /// <b>The data root here is deliberately a GOOD one.</b> An arm that handed
    /// both roots something outside the profile would pass against an
    /// implementation that never looked at the second argument at all.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstallRootOutsideTheProfileIsRefusedEvenWhenTheDataRootIsNot()
    {
        using var good = ScratchDirectory.CreateUnderProfile("install-root-data-ok");
        using var outside = ScratchDirectory.Create("install-root-install-outside");

        // The control first: the same data root with no install root at all is
        // served, so what the arm below catches is the second argument.
        await Assert.That(InstallRootScope.Judge(good.Path, installRoot: null).MayServe).IsTrue();

        var verdict = InstallRootScope.Judge(good.Path, outside.Path);

        await Assert.That(verdict.MayServe).IsFalse();

        var refusal = verdict.Refusal!;

        // It names the root at fault, and it says which KIND of root that is --
        // the whole reason the sentence is parameterised.
        await Assert.That(refusal).Contains(outside.Path);
        await Assert.That(refusal).Contains("install root");

        // Both roots, always, because two levers move two roots and a reader has
        // to be able to tell which sentence is about which.
        await Assert.That(refusal).Contains(good.Path);

        // The remedy that can actually move an install root, and the one that
        // cannot -- asserted in both directions, because a refusal offering
        // BROWSERAI_ROOT here would send somebody to change a setting that has
        // no effect on the thing being refused.
        await Assert.That(refusal).Contains("--installto");
        await Assert.That(refusal).Contains("it moves the data root and never the install root");
        await Assert.That(refusal).Contains("live-instance set");
        await Assert.That(refusal.Contains(Profile, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    /// <summary>
    /// An install root inside the profile is served, and so is the ordinary
    /// installed layout.
    /// </summary>
    /// <remarks>
    /// <b>The false-positive half, for the second root.</b> Every alias arm above
    /// is about the data root; this one is the assurance that the default
    /// installed arrangement -- <c>%LocalAppData%\BrowserAI.app</c> beside
    /// <c>%LocalAppData%\BrowserAI</c> -- is not refused by the check that was
    /// just added, which would stop every installed BrowserAI on the machine.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstalledLayoutIsServedAndSoIsAnyInstallRootInsideTheProfile()
    {
        using var inside = ScratchDirectory.CreateUnderProfile("install-root-install-inside");

        var scratch = InstallRootScope.Judge(BrowserAiPaths.Real.RootAppDir, inside.Path);

        await Assert.That(scratch.MayServe).IsTrue();
        await Assert.That(scratch.Refusal).IsNull();
        await Assert.That(scratch.Unestablished).IsNull();

        // The real shipped pair, composed from the product's own data root rather
        // than spelled here: the install root Velopack puts beside it under the
        // pack id. If this is ever refused, every installed BrowserAI stops
        // starting.
        var installed = BrowserAiPaths.Real.RootAppDir + ".app";

        var real = InstallRootScope.Judge(BrowserAiPaths.Real.RootAppDir, installed);

        await Assert.That(real.MayServe).IsTrue();
        await Assert.That(real.Refusal).IsNull();
    }

    /// <summary>
    /// A data root that is itself refused is reported before the install root is
    /// looked at.
    /// </summary>
    /// <remarks>
    /// <b>Order is asserted rather than left to whichever check happened to run
    /// first.</b> A process that may not keep its browsers where it resolved them
    /// has nothing useful to say about where its binary lives, and a refusal
    /// naming the install root would send somebody to reinstall over a problem a
    /// variable caused.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhenBothRootsAreOutsideTheProfileTheDataRootIsTheOneRefused()
    {
        using var badData = ScratchDirectory.Create("install-root-both-data");
        using var badInstall = ScratchDirectory.Create("install-root-both-install");

        var verdict = InstallRootScope.Judge(badData.Path, badInstall.Path);

        await Assert.That(verdict.MayServe).IsFalse();

        var refusal = verdict.Refusal!;

        await Assert.That(refusal).Contains($"will not serve out of the data root '{badData.Path}'");
        await Assert.That(refusal).Contains(Program.AppRootVariable);

        // Both are still named, so the second problem is not hidden by the first
        // being the one refused.
        await Assert.That(refusal).Contains(badInstall.Path);
    }
}

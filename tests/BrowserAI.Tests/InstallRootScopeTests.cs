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
/// the <c>\\?\</c> prefix. Every one of them is built for real here — no
/// stand-ins — because a predicate checked only against strings a test invented
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
        var verdict = InstallRootScope.Judge(BrowserAiPaths.Real.RootAppDir);

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

        var verdict = InstallRootScope.Judge(outside.Path);

        await Assert.That(verdict.MayServe).IsFalse();

        var refusal = verdict.Refusal!;

        await Assert.That(refusal).Contains(outside.Path);
        await Assert.That(refusal).Contains(Program.AppRootVariable);
        await Assert.That(refusal).Contains("live-instance set");
        await Assert.That(refusal).Contains("install-to flag");

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

        var verdict = InstallRootScope.Judge(inside.Path);

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
        await Assert.That(InstallRootScope.Judge(absent).MayServe).IsTrue();

        using var outside = ScratchDirectory.Create("install-root-absent-outside");

        // And the same arm the other way, so this is not passing because
        // everything absent is accepted.
        await Assert.That(InstallRootScope.Judge(Path.Combine(outside.Path, "not", "created", "yet")).MayServe).IsFalse();
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

        var verdict = InstallRootScope.Judge(link);

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

        await Assert.That(InstallRootScope.Judge(link).MayServe).IsTrue();
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

        await Assert.That(InstallRootScope.Judge(alias.PathTo("app-root")).MayServe).IsTrue();
    }

    /// <summary>
    /// An 8.3 short spelling of a profile path, and the <c>\\?\</c> extended
    /// spelling of one, are both served.
    /// </summary>
    /// <remarks>
    /// The short-name arm carries its own control: 8.3 generation is a per-volume
    /// setting, and on a volume with it disabled
    /// <see cref="PathAliases.ShortNameOf"/> answers the long path unchanged —
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

        await Assert.That(InstallRootScope.Judge(shortName).MayServe).IsTrue();

        // ⚠️ Not a skip and not a branch in the assertion: both spellings must be
        // served, and the only thing the volume's setting changes is whether the
        // two strings differ. Recorded rather than asserted, because a machine
        // with 8.3 generation off is a supported one.
        await Assert.That(
            $"short name {(string.Equals(shortName, target.Path, StringComparison.OrdinalIgnoreCase) ? "is not generated on this volume" : "differs from the long path")}")
            .IsNotEmpty();

        await Assert.That(InstallRootScope.Judge(BrowserAI.Interop.VolumeIdentity.ExtendedLengthPrefix + target.Path).MayServe).IsTrue();
    }

    /// <summary>
    /// The published binary really refuses: it exits non-zero, it writes the
    /// refusal into the process log, and it creates nothing else under the
    /// root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Through the front door, because the ordering inside <c>Main</c> is
    /// half the property.</b> The check sits after the log — the log is the only
    /// channel a refusal has, since <c>stdout</c> is the protocol and
    /// <c>System.Console</c> is banned outright — and before the sweep, the live
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
        // started. It is also the hang detector — a build in which the check was
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

        await Assert.That(said).Contains("will not serve out of the app root");
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
    /// a refusal. The wall clock is not asserted — that would be asserting the
    /// speed of the machine — but the refusal is, and it is the same ordering
    /// `CanonicalPath` is built on.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AUncRootIsRefusedOnItsSpellingAlone()
    {
        var verdict = InstallRootScope.Judge(@"\\10.255.255.1\browserai\root");

        await Assert.That(verdict.MayServe).IsFalse();
        await Assert.That(verdict.Refusal!).Contains("UNC");
    }
}

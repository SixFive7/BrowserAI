// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// The charter's founding sentence, which nothing implemented until 2026-08-16:
/// <i>"registered once at system or user scope, available in every repository,
/// with no per-repo files"</i>
/// ([DECISIONS](../../DECISIONS.md#locking-logging-versioning-and-registration)).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two layers, and the split is the whole design.</b> Everything above
/// <see cref="IRegistrationCommand"/> runs against a double, because a Velopack
/// fast-exit hook is a context no test host can enter -- the same reason the
/// update lane is driven through <c>IUpdateClient</c>. What only the real client
/// can answer is asked of the real client, in the same run, against a
/// <b>scratch configuration directory</b>.
/// </para>
/// <para>
/// ⚠️ <b>Nothing here may touch the maintainer's own MCP configuration, and that
/// is asserted rather than intended.</b> The real-client arms point
/// <c>CLAUDE_CONFIG_DIR</c> at a scratch directory under <c>.work\</c> and then
/// prove the negative: the user's own configuration file does not contain the
/// path this test registered, and that path carries a GUID, so it cannot be
/// there by coincidence.
/// </para>
/// <para>
/// ⚠️ <b><c>[NotInParallel]</c> with no key, on the class, which in TUnit means
/// these arms run beside nothing at all.</b> <i>Corrected 2026-09-15 (previously
/// a <c>ClientGroup</c> key on the two arms that open a scope: "they both write
/// one process-wide environment variable -- <c>CLAUDE_CONFIG_DIR</c> -- and
/// then start a process that reads it. Two at once would each register into the
/// other's scratch directory ... every member writes the same variable and
/// nothing else in the suite starts the client".)</i> Every sentence of that was
/// true and the conclusion did not follow: a key holds an arm apart from the
/// arms carrying the <b>same key</b>, and a process-wide variable is read by
/// <b>every child any arm in the suite starts</b> -- none of which holds a key,
/// and none of which can be enumerated. The measured failure is
/// <see cref="RealInstallerTests"/>' -- the sibling member of the old group,
/// whose <c>BROWSERAI_ROOT</c> reached three unrelated arms' browsers. The same
/// hazard is here: a <c>claude</c> CLI started by anything else during the
/// window would read this scratch configuration directory. <b>The class carries
/// it rather than the two arms</b>, so an arm added later inherits the rule
/// instead of having to remember it, and
/// <see cref="HouseRuleTests.EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing"/>
/// fails the build if this file ever loses it.
/// </para>
/// </remarks>
[NotInParallel]
internal sealed class RegistrationTests
{
    /// <summary>
    /// The client's configuration-directory override, which is what makes a real
    /// registration testable without writing into the user's own file.
    /// </summary>
    internal const string ConfigDirectoryVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>The file the client keeps its user-scoped configuration in.</summary>
    private const string ConfigFileName = ".claude.json";

    // ---- What is registered: never the execution stub -----------------------

    /// <summary>
    /// The stub is refused and the binary inside <c>current\</c> is taken,
    /// decided from the path alone.
    /// </summary>
    /// <remarks>
    /// §G landmine 3. The stub is <b>392,704 bytes</b> beside a
    /// <b>17,853,952-byte</b> binary, is compiled as a Windows-subsystem
    /// executable and <b>exits in 59 ms</b> while the app runs on -- so a client
    /// registered against it watches its MCP server die at the handshake, every
    /// time, with nothing in any log to say why.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheExecutionStubIsRefusedAndTheBinaryInsideCurrentIsRegistered()
    {
        using var install = ScratchDirectory.Create("registration-stub");

        var root = install.Path;
        var app = InstalledLayout.Create(root);

        await Assert.That(RegistrationTarget.TryResolve(app, out var target, out _)).IsTrue();
        await Assert.That(target!.Command).IsEqualTo(InstalledLayout.ServerIn(root));
        await Assert.That(target.InstallRoot).IsEqualTo(root);

        // The stub, which sits directly under the root. It is refused on the
        // SHAPE of its path, before anything on disk is opened, which is why
        // this arm never has to write one.
        await Assert.That(RegistrationTarget.TryResolve(
            Path.Combine(root, RegistrationTarget.AppFileName), out var stub, out var refusal)).IsFalse();
        await Assert.That(stub).IsNull();
        await Assert.That(refusal).Contains("current");
        await Assert.That(refusal).Contains("59 ms");
    }

    /// <summary>
    /// The sibling server is what a client is given, and the app that composed
    /// it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The guarantee this replaces was stronger and is gone, 2026-09-15
    /// (previously: "BrowserAI registers the executable it is itself running
    /// from ... the registered path and the running binary cannot disagree: they
    /// are the same string").</b> The hooks run on the Velopack main exe, and
    /// from this day the main exe is the configuration app. So the path handed
    /// to a client is <b>composed</b> -- the running image's directory plus the
    /// server's file name -- and a composed path is a guess until something
    /// checks it.
    /// </para>
    /// <para>
    /// <b>What replaces it is two checks and a refusal</b>: the file must be
    /// there, and it must declare the console subsystem. Registering the app
    /// under the server's name would put a window on the screen every time a
    /// client opened a session, and the client would then wait forever for a
    /// handshake from a process that is showing a dialog.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSiblingServerIsRegisteredRatherThanTheAppThatComposedIt()
    {
        using var install = ScratchDirectory.Create("registration-sibling");

        var app = InstalledLayout.Create(install.Path);

        await Assert.That(RegistrationTarget.TryResolve(app, out var target, out var refusal)).IsTrue();
        await Assert.That(refusal).IsEmpty();
        await Assert.That(target!.Command).IsEqualTo(InstalledLayout.ServerIn(install.Path));
        await Assert.That(target.Command).IsNotEqualTo(app);
        await Assert.That(target.InstallRoot).IsEqualTo(install.Path);
    }

    /// <summary>
    /// A <c>current\</c> with no server in it registers nothing and says which
    /// file it went looking for.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstallWithNoServerBesideTheAppIsRefusedByName()
    {
        using var install = ScratchDirectory.Create("registration-no-server");

        var app = InstalledLayout.CreateWithoutTheServer(install.Path);

        await Assert.That(RegistrationTarget.TryResolve(app, out var target, out var refusal)).IsFalse();
        await Assert.That(target).IsNull();
        await Assert.That(refusal).Contains(RegistrationTarget.ServerFileName);
        await Assert.That(refusal).Contains(InstalledLayout.ServerIn(install.Path));
    }

    /// <summary>
    /// A file wearing the server's name that is not a console binary is refused,
    /// and so is one that is not an executable at all.
    /// </summary>
    /// <remarks>
    /// <b>The name is not the check and cannot be.</b> Both arms here put a file
    /// at exactly the path the composition produces; what separates them from
    /// the passing case is a field the linker writes, which is why
    /// <see cref="PeSubsystem"/> reads it rather than trusting the extension.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFileAtTheServersNameThatIsNotAConsoleBinaryIsRefused()
    {
        using var install = ScratchDirectory.Create("registration-wrong-subsystem");

        var app = InstalledLayout.CreateWithoutTheServer(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);

        // A second copy of the configuration app, renamed.
        InstalledLayout.WritePortableExecutable(server, PeSubsystem.WindowsGui);

        await Assert.That(RegistrationTarget.TryResolve(app, out var windows, out var windowsRefusal)).IsFalse();
        await Assert.That(windows).IsNull();
        await Assert.That(windowsRefusal).Contains("Windows-subsystem");

        InstalledLayout.WriteSomethingThatIsNotAnExecutable(server);

        await Assert.That(RegistrationTarget.TryResolve(app, out var garbage, out var garbageRefusal)).IsFalse();
        await Assert.That(garbage).IsNull();
        await Assert.That(garbageRefusal).Contains("no readable PE header");
    }

    /// <summary>A path that cannot be resolved is refused rather than guessed at.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APathThatIsNotAnInstalledBrowserAiIsRefused()
    {
        await Assert.That(RegistrationTarget.TryResolve(null, out _, out var noPath)).IsFalse();
        await Assert.That(noPath).Contains("no path");

        await Assert.That(RegistrationTarget.TryResolve(@"current\BrowserAI.exe", out _, out var relative)).IsFalse();
        await Assert.That(relative).Contains("fully qualified");

        // Case is not what decides it: the directory is `current` either way.
        using var shoutingInstall = ScratchDirectory.Create("registration-shouting");
        var shoutingCurrent = Directory.CreateDirectory(Path.Combine(shoutingInstall.Path, "CURRENT"));

        InstalledLayout.WritePortableExecutable(
            Path.Combine(shoutingCurrent.FullName, RegistrationTarget.AppFileName), PeSubsystem.WindowsGui);
        InstalledLayout.WritePortableExecutable(
            Path.Combine(shoutingCurrent.FullName, RegistrationTarget.ServerFileName), PeSubsystem.WindowsCui);

        await Assert.That(RegistrationTarget.TryResolve(
            Path.Combine(shoutingCurrent.FullName, RegistrationTarget.AppFileName), out var shouting, out _)).IsTrue();
        await Assert.That(shouting!.InstallRoot).IsEqualTo(shoutingInstall.Path);
    }

    // ---- Idempotence, which the client does not supply ----------------------

    /// <summary>
    /// Install, update, repair and reinstall converge on exactly one
    /// registration.
    /// </summary>
    /// <remarks>
    /// <b>The client's <c>add</c> is not idempotent</b> -- measured 2026-08-16 @
    /// 2.1.233, a second one exits 1 -- so this property belongs to BrowserAI and
    /// is asserted here over a double that models exactly that behaviour.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InstallUpdateRepairAndReinstallProduceExactlyOneRegistration()
    {
        var client = new FakeClientCommandLine();
        var (logger, _) = Capture();

        // A real layout on disk, because the command is composed from it and
        // checked against it now. Everything else here is still a double.
        using var install = ScratchDirectory.Create("registration-idempotence");
        var command = InstalledLayout.Create(install.Path);

        // ⚠️ The update reads what the DOUBLE holds, 2026-09-15. Without the
        // seam it reads the real client's own configuration file, and this arm
        // would then pass or fail on what the machine happens to have
        // registered. It is not a question this arm is asking.
        var seen = WhatTheDoubleHolds(client);

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Install, command, client, logger, seen).Status)
            .IsEqualTo(RegistrationStatus.Registered);

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Update, command, client, logger, seen).Status)
            .IsEqualTo(RegistrationStatus.AlreadyRegistered);

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Install, command, client, logger, seen).Status)
            .IsEqualTo(RegistrationStatus.Registered);

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Update, command, client, logger, seen).Status)
            .IsEqualTo(RegistrationStatus.AlreadyRegistered);

        await Assert.That(client.Registered.Count).IsEqualTo(1);

        // The SERVER, not the app the hook ran as. `command` is the image path
        // a hook is handed; what a client is given is its sibling.
        await Assert.That(client.Registered[McpClientRegistration.ServerName])
            .IsEqualTo(InstalledLayout.ServerIn(install.Path));
    }

    /// <summary>
    /// An install re-points a registration of ours; an update leaves one alone;
    /// neither touches one that is not ours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The only judgement that differs between the two intents, and both
    /// directions cost something real.</b> Re-pointing on every update would
    /// silently delete arguments a user added to their own registration;
    /// never re-pointing would leave a stale path after a
    /// <c>Setup.exe --installto</c> elsewhere, which is a registration that
    /// launches nothing.
    /// </para>
    /// <para>
    /// ⚠️ <b>Widened 2026-09-16</b> <i>(previously "An install re-points an
    /// existing registration", asserted over an entry outside this install
    /// root).</i> <b>Existing</b> was doing two jobs in that sentence. An entry
    /// under this install root is ours and is re-pointed; an entry anywhere else
    /// is another BrowserAI's, and an install that re-pointed it was one product
    /// overwriting another's configuration. Both halves are asserted here now,
    /// and the wider statement of the rule is
    /// <see cref="NeitherAnInstallNorAnUninstallTouchesAnEntryThisInstallDidNotWrite"/>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstallRePointsAStaleRegistrationAndAnUpdateNeverOverwritesOne()
    {
        var client = new FakeClientCommandLine();
        var (logger, _) = Capture();

        using var install = ScratchDirectory.Create("registration-moved");

        // A path that no longer exists, which is exactly what an entry written
        // by an older install looks like after the binary moved.
        const string Stale = @"C:\somewhere\old\current\BrowserAI.Server.exe";
        client.Registered[McpClientRegistration.ServerName] = Stale;

        var app = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);

        // ⚠️ An update leaves it alone, and since 2026-09-15 it SAYS WHY
        // rather than reporting it as already registered: this path is not under
        // this install root, so it belongs to another BrowserAI and is reported
        // with its location. Untouched either way, which is the property the
        // name of this arm is about.
        var update = McpRegistrar.Apply(RegistrationIntent.Update, app, client, logger, WhatTheDoubleHolds(client));

        await Assert.That(update.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(update.Detail).Contains("Another BrowserAI is registered at");
        await Assert.That(client.Registered[McpClientRegistration.ServerName]).IsEqualTo(Stale);

        // ⚠️ AND SO DOES AN INSTALL, since 2026-09-16. *Corrected 2026-09-16
        // (previously this asserted `Registered` and that the entry had been
        // re-pointed at `server`.)* That WAS the behaviour and it was the defect:
        // `Reassert` ran `mcp remove` and then `mcp add` without reading anything,
        // so installing this BrowserAI deleted another BrowserAI's registration
        // and wrote its own over the top. The re-pointing half of this arm's name
        // is asserted below, where the entry really is ours.
        var refusedInstall = McpRegistrar.Apply(RegistrationIntent.Install, app, client, logger, WhatTheDoubleHolds(client));

        await Assert.That(refusedInstall.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(refusedInstall.Detail).Contains("Another BrowserAI is registered at");
        await Assert.That(client.Registered[McpClientRegistration.ServerName]).IsEqualTo(Stale);

        // Nothing ran on either intent, because both refused.
        await Assert.That(client.Verbs).IsEmpty();

        // ---- and the re-pointing this arm is named for, over an entry that IS
        // ours: same install root, a file that is no longer there.
        var ours = new FakeClientCommandLine();
        var gone = Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, "BrowserAI.exe.old");

        ours.Registered[McpClientRegistration.ServerName] = gone;

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Install, app, ours, logger, WhatTheDoubleHolds(ours)).Status)
            .IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(ours.Registered[McpClientRegistration.ServerName]).IsEqualTo(server);

        // An install removes before it adds.
        await Assert.That(ours.Verbs).IsEquivalentTo(RemoveAdd);
    }

    /// <summary>
    /// The update hook repairs an entry of ours that has gone stale, leaves one
    /// that still resolves exactly as it is, and never touches a foreign one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The three arms are the three states an update can meet</b>, and only
    /// one of them writes. Each is constructed rather than provoked: what is
    /// registered already is handed in, so the arm is about the judgement rather
    /// than about a file the client happens to have.
    /// </para>
    /// <para>
    /// ⚠️ <b>The foreign arm is the one that would be cheapest to get wrong.</b>
    /// An entry named <c>browserai</c> pointing outside this install root is
    /// another BrowserAI, and an update that adopted it would be one product
    /// silently re-pointing another's configuration. It is reported with the
    /// path, and the verb list proves nothing ran.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUpdateRepairsOurOwnStaleEntryAndLeavesEveryOtherKindAlone()
    {
        using var install = ScratchDirectory.Create("registration-repair");

        var app = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var (logger, log) = Capture();

        // ---- ours, and the file it names is gone: re-pointed ----------------
        var stale = Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, "BrowserAI.exe.old");
        var repairing = new FakeClientCommandLine();
        repairing.Registered[McpClientRegistration.ServerName] = stale;

        var repaired = McpRegistrar.Apply(
            RegistrationIntent.Update, app, repairing, logger,
            root => new RegistrationView(
                RegistrationScope.User, "<constructed>", stale, McpRegistryView.Classify(stale, root), null));

        await Assert.That(repaired.Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(repairing.Registered[McpClientRegistration.ServerName]).IsEqualTo(server);
        await Assert.That(log.Records.Any(record => record.Message.Contains("Repaired the MCP registration", StringComparison.Ordinal))).IsTrue();

        // ---- ours, and it still resolves: untouched, arguments and all ------
        var keeping = new FakeClientCommandLine();
        keeping.Registered[McpClientRegistration.ServerName] = server;

        var kept = McpRegistrar.Apply(
            RegistrationIntent.Update, app, keeping, logger,
            root => new RegistrationView(
                RegistrationScope.User, "<constructed>", server, McpRegistryView.Classify(server, root), null));

        await Assert.That(kept.Status).IsEqualTo(RegistrationStatus.AlreadyRegistered);
        await Assert.That(keeping.Registered[McpClientRegistration.ServerName]).IsEqualTo(server);
        await Assert.That(keeping.Verbs).IsEmpty();

        // ---- ours, present, and the CONFIGURATION APP: re-pointed ----------
        // ⚠️ Added 2026-09-16. This is the state every pre-split 1.0.0 install
        // is in, and it is the one the update hook used to leave alone: the
        // entry names `current\BrowserAI.exe`, which is there, and which is now
        // the window rather than the server. A client that starts it gets a
        // dialog and no handshake.
        var misdirecting = new FakeClientCommandLine();
        misdirecting.Registered[McpClientRegistration.ServerName] = app;

        var corrected = McpRegistrar.Apply(
            RegistrationIntent.Update, app, misdirecting, logger,
            root => new RegistrationView(
                RegistrationScope.User, "<constructed>", app, McpRegistryView.Classify(app, root), null));

        await Assert.That(corrected.Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(misdirecting.Registered[McpClientRegistration.ServerName]).IsEqualTo(server);

        // ---- somebody else's: reported, and nothing runs --------------------
        using var elsewhere = ScratchDirectory.Create("registration-repair-foreign");

        _ = InstalledLayout.Create(elsewhere.Path);

        var theirs = InstalledLayout.ServerIn(elsewhere.Path);
        var foreignClient = new FakeClientCommandLine();
        foreignClient.Registered[McpClientRegistration.ServerName] = theirs;

        var foreign = McpRegistrar.Apply(
            RegistrationIntent.Update, app, foreignClient, logger,
            root => new RegistrationView(
                RegistrationScope.User, "<constructed>", theirs, McpRegistryView.Classify(theirs, root), null));

        await Assert.That(foreign.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(foreign.Detail).Contains("Another BrowserAI is registered at");
        await Assert.That(foreign.Detail).Contains(theirs);
        await Assert.That(foreignClient.Registered[McpClientRegistration.ServerName]).IsEqualTo(theirs);
        await Assert.That(foreignClient.Verbs).IsEmpty();
    }

    /// <summary>
    /// A configuration file that cannot be read is never reported as nothing
    /// being registered.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUnreadableConfigurationIsNotReadAsNothingRegistered()
    {
        using var install = ScratchDirectory.Create("registration-unreadable");

        var app = InstalledLayout.Create(install.Path);
        var client = new FakeClientCommandLine();
        var (logger, _) = Capture();

        var report = McpRegistrar.Apply(
            RegistrationIntent.Update, app, client, logger,
            _ => new RegistrationView(
                RegistrationScope.User,
                "<constructed>",
                null,
                RegistrationOwnership.Absent,
                "'<constructed>' is not readable JSON, so what is registered there is unknown."));

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(report.Detail).Contains("unknown");
        await Assert.That(client.Verbs).IsEmpty();
    }

    /// <summary>
    /// An install and an uninstall refuse a foreign entry the way an update
    /// does, and behave exactly as they did over every other state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-09-16, after a review found the ownership check on one
    /// intent out of three.</b> <c>Repair</c> read the client's file and refused
    /// what it did not write; <c>Reassert</c> (the install hook) ran
    /// <c>mcp remove</c> and then <c>mcp add</c> with no check at all, and
    /// <c>Remove</c> (the uninstall hook) ran <c>mcp remove</c> unconditionally.
    /// So installing BrowserAI <b>overwrote</b> another BrowserAI's registration
    /// and uninstalling it <b>deleted</b> one -- which is the exact thing
    /// <see cref="RegistrationOwnership"/>'s own summary, <c>AppState.MayRemove</c>
    /// and the registration row in <c>DECISIONS.md</c> all say this product never
    /// does. Those three sentences were kept true rather than narrowed.
    /// </para>
    /// <para>
    /// <b>Over constructed inputs, like the update arm above.</b> What is
    /// registered already is handed in, so each arm is about the judgement and
    /// not about whatever the machine's own <c>~/.claude.json</c> holds.
    /// </para>
    /// <para>
    /// <b>The states that must NOT have changed are asserted beside the one that
    /// did</b> -- absent, ours-and-present and ours-and-stale all still reassert
    /// on install and still unregister on uninstall. A refusal that fired on
    /// everything would satisfy the foreign arm alone.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NeitherAnInstallNorAnUninstallTouchesAnEntryThisInstallDidNotWrite()
    {
        using var install = ScratchDirectory.Create("registration-ownership");
        using var elsewhere = ScratchDirectory.Create("registration-ownership-foreign");

        var app = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);

        _ = InstalledLayout.Create(elsewhere.Path);

        var theirs = InstalledLayout.ServerIn(elsewhere.Path);
        var (logger, _) = Capture();

        // ---- somebody else's, on install: reported, and nothing runs --------
        var installing = new FakeClientCommandLine();
        installing.Registered[McpClientRegistration.ServerName] = theirs;

        var overwritten = McpRegistrar.Apply(
            RegistrationIntent.Install, app, installing, logger, WhatTheDoubleHolds(installing));

        await Assert.That(overwritten.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(overwritten.Detail).Contains("Another BrowserAI is registered at");
        await Assert.That(overwritten.Detail).Contains(theirs);
        await Assert.That(overwritten.Command).IsEqualTo(theirs);
        await Assert.That(overwritten.IsWhatWasAskedFor).IsFalse();
        await Assert.That(installing.Registered[McpClientRegistration.ServerName]).IsEqualTo(theirs);
        await Assert.That(installing.Verbs).IsEmpty();

        // ---- somebody else's, on uninstall: the same ------------------------
        var uninstalling = new FakeClientCommandLine();
        uninstalling.Registered[McpClientRegistration.ServerName] = theirs;

        var spared = McpRegistrar.Apply(
            RegistrationIntent.Uninstall, app, uninstalling, logger, WhatTheDoubleHolds(uninstalling));

        await Assert.That(spared.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(spared.Detail).Contains("Another BrowserAI is registered at");
        await Assert.That(spared.Detail).Contains(theirs);
        await Assert.That(spared.Command).IsEqualTo(theirs);
        await Assert.That(spared.IsWhatWasAskedFor).IsFalse();
        await Assert.That(uninstalling.Registered[McpClientRegistration.ServerName]).IsEqualTo(theirs);
        await Assert.That(uninstalling.Verbs).IsEmpty();

        // ---- nothing registered: an install still adds ----------------------
        var fresh = new FakeClientCommandLine();

        await Assert.That(McpRegistrar.Apply(
            RegistrationIntent.Install, app, fresh, logger, WhatTheDoubleHolds(fresh)).Status)
            .IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(fresh.Registered[McpClientRegistration.ServerName]).IsEqualTo(server);
        await Assert.That(fresh.Verbs).IsEquivalentTo(RemoveAdd);

        // ---- ours and present: an install still reasserts -------------------
        var ours = new FakeClientCommandLine();
        ours.Registered[McpClientRegistration.ServerName] = server;

        await Assert.That(McpRegistrar.Apply(
            RegistrationIntent.Install, app, ours, logger, WhatTheDoubleHolds(ours)).Status)
            .IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(ours.Registered[McpClientRegistration.ServerName]).IsEqualTo(server);
        await Assert.That(ours.Verbs).IsEquivalentTo(RemoveAdd);

        // ---- ours and stale: an install still re-points ---------------------
        var staleCommand = Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, "BrowserAI.exe.old");
        var stale = new FakeClientCommandLine();
        stale.Registered[McpClientRegistration.ServerName] = staleCommand;

        await Assert.That(McpRegistrar.Apply(
            RegistrationIntent.Install, app, stale, logger, WhatTheDoubleHolds(stale)).Status)
            .IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(stale.Registered[McpClientRegistration.ServerName]).IsEqualTo(server);

        // ---- ours and present: an uninstall still removes -------------------
        var removing = new FakeClientCommandLine();
        removing.Registered[McpClientRegistration.ServerName] = server;

        await Assert.That(McpRegistrar.Apply(
            RegistrationIntent.Uninstall, app, removing, logger, WhatTheDoubleHolds(removing)).Status)
            .IsEqualTo(RegistrationStatus.Unregistered);
        await Assert.That(removing.Registered.ContainsKey(McpClientRegistration.ServerName)).IsFalse();

        // ---- nothing registered: an uninstall says so -----------------------
        var nothing = new FakeClientCommandLine();

        await Assert.That(McpRegistrar.Apply(
            RegistrationIntent.Uninstall, app, nothing, logger, WhatTheDoubleHolds(nothing)).Status)
            .IsEqualTo(RegistrationStatus.NothingToUnregister);

        // ---- and a file nobody could read acts on nothing, either way -------
        foreach (var intent in new[] { RegistrationIntent.Install, RegistrationIntent.Uninstall })
        {
            var blind = new FakeClientCommandLine();
            blind.Registered[McpClientRegistration.ServerName] = server;

            var refused = McpRegistrar.Apply(
                intent, app, blind, logger,
                _ => new RegistrationView(
                    RegistrationScope.User,
                    "<constructed>",
                    null,
                    RegistrationOwnership.Absent,
                    "'<constructed>' is not readable JSON, so what is registered there is unknown."));

            await Assert.That(refused.Status).IsEqualTo(RegistrationStatus.Refused);
            await Assert.That(refused.Detail).Contains("unknown");
            await Assert.That(blind.Verbs).IsEmpty();
        }
    }

    /// <summary>
    /// The four states the reader reports, over files it wrote itself.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheReaderTellsAbsentFromOursFromStaleFromForeign()
    {
        using var config = ScratchDirectory.Create("registry-view");
        using var install = ScratchDirectory.Create("registry-view-install");

        _ = InstalledLayout.Create(install.Path);

        var server = InstalledLayout.ServerIn(install.Path);
        var file = Path.Combine(config.Path, McpRegistryView.UserConfigFileName);

        // No file at all.
        var absent = McpRegistryView.Read(RegistrationScope.User, file, install.Path);

        await Assert.That(absent.Ownership).IsEqualTo(RegistrationOwnership.Absent);
        await Assert.That(absent.Unreadable).IsNull();

        // Ours, present.
        await WriteServerEntryAsync(file, server);

        var ours = McpRegistryView.Read(RegistrationScope.User, file, install.Path);

        await Assert.That(ours.Ownership).IsEqualTo(RegistrationOwnership.OursAndPresent);
        await Assert.That(ours.Command).IsEqualTo(server);

        // ⚠️ OURS, PRESENT, AND THE WRONG BINARY -- 2026-09-16. Every
        // registration written before the two-binary split names
        // `current\BrowserAI.exe`, which since 2026-09-15 is the CONFIGURATION
        // APP. The file is there, so a classifier that asks only whether it
        // exists answers "ours and present", the update hook leaves it exactly
        // as it is, and the client then starts a window and waits forever for a
        // JSON-RPC handshake from a process that is showing a dialog -- with
        // nothing in any log, because nothing failed.
        //
        // What makes an entry OURS is the install root. What makes it PRESENT is
        // being the SERVER, read out of the file's own PE subsystem rather than
        // taken from its name, which is the same discriminator
        // RegistrationTarget uses when it composes the path in the first place.
        var theApp = Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);

        await Assert.That(File.Exists(theApp)).IsTrue();
        await Assert.That(PeSubsystem.Of(theApp)).IsEqualTo(PeSubsystem.WindowsGui);

        await WriteServerEntryAsync(file, theApp);

        var misdirected = McpRegistryView.Read(RegistrationScope.User, file, install.Path);

        await Assert.That(misdirected.Ownership).IsEqualTo(RegistrationOwnership.OursAndStale);
        await Assert.That(misdirected.Command).IsEqualTo(theApp);

        // And a file under our root that is not a portable executable at all
        // gets the same answer, because the question is never "is something
        // there" -- it is "may this be launched as the server".
        var notAnExecutable = Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, "BrowserAI.Server.exe.bak");

        InstalledLayout.WriteSomethingThatIsNotAnExecutable(notAnExecutable);
        await WriteServerEntryAsync(file, notAnExecutable);

        await Assert.That(McpRegistryView.Read(RegistrationScope.User, file, install.Path).Ownership)
            .IsEqualTo(RegistrationOwnership.OursAndStale);

        // Ours, stale.
        await WriteServerEntryAsync(
            file, Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, "BrowserAI.exe.gone"));

        await Assert.That(McpRegistryView.Read(RegistrationScope.User, file, install.Path).Ownership)
            .IsEqualTo(RegistrationOwnership.OursAndStale);

        // Somebody else's.
        await WriteServerEntryAsync(file, Path.Combine("D:", "someone", "else", "current", RegistrationTarget.ServerFileName));

        await Assert.That(McpRegistryView.Read(RegistrationScope.User, file, install.Path).Ownership)
            .IsEqualTo(RegistrationOwnership.Foreign);

        // Not readable JSON: an answer of its own, never "absent".
        await File.WriteAllTextAsync(file, "{ this is not json");

        var broken = McpRegistryView.Read(RegistrationScope.User, file, install.Path);

        await Assert.That(broken.Ownership).IsEqualTo(RegistrationOwnership.Absent);
        await Assert.That(broken.Unreadable).IsNotNull();
        await Assert.That(broken.Unreadable!).Contains("unknown");
    }

    /// <summary>
    /// The portable form is what a project file gets, and it expands to the
    /// install it was written for.
    /// </summary>
    /// <remarks>
    /// <b>An absolute path under one person's profile is wrong on every
    /// teammate's machine</b>, which makes committing one worse than committing
    /// nothing. The expansion is asserted against this machine's own
    /// <c>%LOCALAPPDATA%</c> so that the form cannot drift from what the client
    /// would resolve.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheProjectScopeCommandIsPortableAndExpandsToTheDefaultInstall()
    {
        const string PackId = "BrowserAI.app";

        var portable = McpClientRegistration.PortableCommandFor(PackId);

        await Assert.That(portable).StartsWith("${LOCALAPPDATA}/");
        await Assert.That(portable).EndsWith(RegistrationTarget.ServerFileName);
        await Assert.That(portable).DoesNotContain(BackslashText);

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            PackId,
            RegistrationTarget.CurrentDirectoryName,
            RegistrationTarget.ServerFileName);

        await Assert.That(Path.GetFullPath(McpRegistryView.Expand(portable))).IsEqualTo(expected);

        // A name nothing defines is left exactly as it stands rather than
        // collapsing to an empty segment, which would silently produce a path
        // that resolves somewhere.
        await Assert.That(McpRegistryView.Expand("${BROWSERAI_NO_SUCH_VARIABLE}/x"))
            .IsEqualTo("${BROWSERAI_NO_SUCH_VARIABLE}/x");

        // And the scope really is the only difference in the call.
        await Assert.That(McpClientRegistration.AddArguments("c", McpClientRegistration.ProjectScope))
            .IsEquivalentTo(ProjectScopeAdd);
    }

    /// <summary>A single backslash, spelled once.</summary>
    private const string BackslashText = "\\";

    /// <summary>What a project-scope add looks like on the wire.</summary>
    private static readonly string[] ProjectScopeAdd =
        ["mcp", "add", McpClientRegistration.ServerName, "--scope", "project", "--", "c"];

    private static Task WriteServerEntryAsync(string file, string command) =>
        File.WriteAllTextAsync(
            file,
            "{\n  \"mcpServers\": {\n    \""
                + McpClientRegistration.ServerName
                + "\": {\n      \"type\": \"stdio\",\n      \"command\": "
                + System.Text.Json.JsonSerializer.Serialize(command)
                + ",\n      \"args\": [],\n      \"env\": {}\n    }\n  }\n}\n");

    /// <summary>
    /// An uninstall removes the registration, and an already-absent one is not a
    /// failure.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallRemovesTheRegistrationAndAnAbsentOneIsNotAFailure()
    {
        var client = new FakeClientCommandLine();
        var (logger, log) = Capture();

        using var install = ScratchDirectory.Create("registration-uninstall");
        var command = InstalledLayout.Create(install.Path);

        _ = McpRegistrar.Apply(RegistrationIntent.Install, command, client, logger, WhatTheDoubleHolds(client));

        var removed = McpRegistrar.Apply(RegistrationIntent.Uninstall, command, client, logger, WhatTheDoubleHolds(client));

        await Assert.That(removed.Status).IsEqualTo(RegistrationStatus.Unregistered);
        await Assert.That(client.Registered).IsEmpty();

        var again = McpRegistrar.Apply(RegistrationIntent.Uninstall, command, client, logger, WhatTheDoubleHolds(client));

        await Assert.That(again.Status).IsEqualTo(RegistrationStatus.NothingToUnregister);
        await Assert.That(again.IsWhatWasAskedFor).IsTrue();
        await Assert.That(log.Logged("There was no 'browserai' registered")).IsTrue();
    }

    // ---- Failing visibly, and never failing the install ---------------------

    /// <summary>
    /// A machine with no MCP client is reported, with the command to run once
    /// there is one.
    /// </summary>
    /// <remarks>
    /// <b>Warning, not error, and never a throw.</b> An installer that failed
    /// because the user has no MCP client would be worse than the state it was
    /// protecting against -- but an installed BrowserAI nothing is configured to
    /// talk to is exactly the state this whole mechanism exists to end, so it is
    /// never silent either.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AMachineWithNoClientIsReportedRatherThanFailedOrIgnored()
    {
        var client = new FakeClientCommandLine { Executable = null };
        var (logger, log) = Capture();

        using var install = ScratchDirectory.Create("registration-no-client");

        var report = McpRegistrar.Apply(RegistrationIntent.Install, InstalledLayout.Create(install.Path), client, logger);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.ClientNotFound);
        await Assert.That(report.IsWhatWasAskedFor).IsTrue();
        await Assert.That(report.Detail).Contains("claude mcp add browserai --scope user");
        await Assert.That(client.Invocations).IsEmpty();

        var warning = log.Records.Single(record => record.EventId.Id is 5);

        await Assert.That(warning.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(warning.Message).Contains("nothing is configured to talk to it");
    }

    /// <summary>
    /// A client that refuses, one that hangs, and one that cannot be started at
    /// all are each named with the command to run by hand.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientThatDoesNotDoWhatWasAskedIsNamedWithTheManualCommand()
    {
        using var install = ScratchDirectory.Create("registration-client-said-no");
        var command = InstalledLayout.Create(install.Path);

        var refused = new FakeClientCommandLine { Always = new CommandOutcome(2, "some other failure", TimedOut: false, null) };
        var (logger, log) = Capture();

        var report = McpRegistrar.Apply(RegistrationIntent.Install, command, refused, logger, WhatTheDoubleHolds(refused));

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(report.IsWhatWasAskedFor).IsFalse();
        await Assert.That(report.Detail).Contains("exited 2");
        await Assert.That(report.Detail).Contains("some other failure");
        await Assert.That(report.Detail).Contains($"mcp add {McpClientRegistration.ServerName} --scope user");
        await Assert.That(log.Records.Any(record => record.Level is LogLevel.Error)).IsTrue();

        var hanging = new FakeClientCommandLine { Always = new CommandOutcome(-1, string.Empty, TimedOut: true, null) };
        var stalled = McpRegistrar.Apply(RegistrationIntent.Install, command, hanging, logger, WhatTheDoubleHolds(hanging));

        await Assert.That(stalled.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(stalled.Detail).Contains("did not finish within 10s");

        var dead = new FakeClientCommandLine { Always = new CommandOutcome(-1, string.Empty, TimedOut: false, "Access is denied") };
        var unstartable = McpRegistrar.Apply(RegistrationIntent.Install, command, dead, logger, WhatTheDoubleHolds(dead));

        await Assert.That(unstartable.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(unstartable.Detail).Contains("Access is denied");
    }

    /// <summary>
    /// Nothing a client does can throw into the installer.
    /// </summary>
    /// <remarks>
    /// A hook that throws breaks the install. This is the boundary that makes
    /// that impossible, so it is asserted rather than reviewed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AThrowingClientIsCaughtRatherThanBreakingTheInstall()
    {
        var client = new FakeClientCommandLine { Throws = true };
        var (logger, log) = Capture();

        using var install = ScratchDirectory.Create("registration-throwing");

        var report = McpRegistrar.Apply(RegistrationIntent.Install, InstalledLayout.Create(install.Path), client, logger, WhatTheDoubleHolds(client));

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(report.Detail).Contains("asked to throw");
        await Assert.That(log.Records.Any(record => record.EventId.Id is 8)).IsTrue();
    }

    /// <summary>
    /// A refusal to register still produces a log record and a report; it never
    /// registers the wrong thing quietly.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusedPathIsLoggedAtErrorAndTheClientIsNeverStarted()
    {
        var client = new FakeClientCommandLine();
        var (logger, log) = Capture();

        var report = McpRegistrar.Apply(RegistrationIntent.Install, @"C:\install\BrowserAI.exe", client, logger);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(client.Invocations).IsEmpty();
        await Assert.That(log.Records.Single(record => record.EventId.Id is 6).Level).IsEqualTo(LogLevel.Error);
    }

    // ---- The state a person can find ---------------------------------------

    /// <summary>
    /// The hook writes its outcome where a person looking for it will find it:
    /// in the <b>data</b> root, and nothing at all under the install root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Re-pointed 2026-09-15 (previously "beside <c>current\</c> rather
    /// than inside it ... an update replaces that directory wholesale").</b> The
    /// right rule, aimed one level too low. A sibling of <c>current\</c> is still
    /// inside the install root, and the two events a person reads this file
    /// after are the two that empty it: a repair install, which renames the root
    /// aside and deletes it, and an uninstall. The assertion is the stronger one
    /// now -- the record and the log are not under the install root at all, and
    /// the hook leaves that root untouched.
    /// </para>
    /// <para>
    /// <b>The data root is a scratch directory here, and it has to be.</b> The
    /// product resolves a constant under <c>%LocalAppData%</c>; a test that let
    /// it do so would write a registration record into the developer's own data
    /// root -- which is why the overload the suite drives takes the seam rather
    /// than defaulting it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHookWritesItsOutcomeIntoTheDataRootAndNothingUnderTheInstallRoot()
    {
        using var install = ScratchDirectory.Create("registration-hook");
        using var data = ScratchDirectory.Create("registration-hook-data");

        // ⚠️ THE CLIENT'S CONFIGURATION IS SCRATCH, and since 2026-09-16 that is
        // a requirement rather than tidiness. HookRegistration.Run has no seam
        // over what is registered already, and the registrar now reads it on
        // EVERY intent -- so without this the hook asks about the maintainer's
        // own ~/.claude.json, finds an entry foreign to this scratch install
        // root, and refuses. The read is the product's; the file it reads is
        // this arm's.
        using var clientConfig = ScratchDirectory.Create("registration-hook-data-config");
        using var pointed = PointTheClientAt(clientConfig.Path);

        var command = InstalledLayout.Create(install.Path);
        var client = new FakeClientCommandLine();

        var before = Directory
            .EnumerateFileSystemEntries(install.Path, "*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(install.Path, entry))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var report = HookRegistration.Run(
            RegistrationIntent.Install,
            "9.9.9",
            command,
            client,
            new LocalAppDataPaths(data.Path)).Registration;

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Registered);

        var record = Path.Combine(data.Path, RegistrationRecord.FileName);

        await Assert.That(File.Exists(record)).IsTrue();
        await Assert.That(record.Contains(@"\current\", StringComparison.OrdinalIgnoreCase)).IsFalse();

        var written = await File.ReadAllTextAsync(record);

        await Assert.That(written).Contains("\"outcome\": \"Registered\"");
        await Assert.That(written).Contains("\"scope\": \"user\"");
        await Assert.That(written).Contains("\"browserAiVersion\": \"9.9.9\"");
        await Assert.That(written).Contains("\"isWhatWasAskedFor\": true");

        // The hook's own log is written inside the hook, because VelopackApp.Run
        // exits the process when it has served one -- anything merely buffered
        // is discarded at that exit.
        var logs = Directory.EnumerateFiles(Path.Combine(data.Path, "logs"), "*.log").ToList();

        await Assert.That(logs).IsNotEmpty();

        var text = string.Join("\n", logs.Select(ReadShared));

        await Assert.That(text).Contains("Velopack Install hook running for BrowserAI 9.9.9");
        await Assert.That(text).Contains($"Registered '{McpClientRegistration.ServerName}'");

        // ⚠️ THE HALF THAT IS RED AGAINST THE OLD LAYOUT: the install root the
        // image path names is a directory Setup.exe renames aside and deletes,
        // and the hook leaves nothing whatever in it.
        //
        // ⚠️ Narrowed 2026-09-15 (previously the install root was expected to be
        // EMPTY). It is not empty any more and cannot be: the two executables
        // have to be on disk for the composed server path to be checkable at
        // all, so this arm builds a `current\` before it runs. What is asserted
        // is the property that was always meant -- the hook ADDS nothing -- and it
        // is taken as a difference against what was there first, so a file the
        // hook writes anywhere under that root fails it exactly as before.
        var afterwards = Directory
            .EnumerateFileSystemEntries(install.Path, "*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(install.Path, entry))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        afterwards.ExceptWith(before);

        await Assert.That(string.Join(", ", afterwards.Order(StringComparer.Ordinal))).IsEmpty();
    }

    /// <summary>
    /// A hook whose registration failed still says so on disk.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFailedRegistrationIsRecordedRatherThanLeavingTheInstallSilent()
    {
        using var install = ScratchDirectory.Create("registration-hook-failed");
        using var data = ScratchDirectory.Create("registration-hook-failed-data");

        // ⚠️ THE CLIENT'S CONFIGURATION IS SCRATCH, and since 2026-09-16 that is
        // a requirement rather than tidiness. HookRegistration.Run has no seam
        // over what is registered already, and the registrar now reads it on
        // EVERY intent -- so without this the hook asks about the maintainer's
        // own ~/.claude.json, finds an entry foreign to this scratch install
        // root, and refuses. The read is the product's; the file it reads is
        // this arm's.
        using var clientConfig = ScratchDirectory.Create("registration-hook-failed-data-config");
        using var pointed = PointTheClientAt(clientConfig.Path);

        var command = InstalledLayout.Create(install.Path);
        var client = new FakeClientCommandLine { Executable = null };

        var report = HookRegistration.Run(
            RegistrationIntent.Install,
            "9.9.9",
            command,
            client,
            new LocalAppDataPaths(data.Path)).Registration;

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.ClientNotFound);

        var written = await File.ReadAllTextAsync(Path.Combine(data.Path, RegistrationRecord.FileName));

        await Assert.That(written).Contains("\"outcome\": \"ClientNotFound\"");
        await Assert.That(written).Contains("claude mcp add browserai --scope user");
    }

    // ---- The data root at uninstall -----------------------------------------

    /// <summary>
    /// A silent uninstall keeps the data root and never puts a window on
    /// anybody's screen.
    /// </summary>
    /// <remarks>
    /// <b>Both halves, and the second is the one that would break a scripted
    /// uninstall.</b> A dialog inside <c>Update.exe --uninstall --silent</c> is a
    /// 60-second stall ending in the hook being killed -- so the assertion is not
    /// only that the directory survived, it is that the question was never
    /// asked at all.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASilentUninstallKeepsTheDataRootAndNeverAsks()
    {
        using var install = ScratchDirectory.Create("uninstall-silent");
        using var data = ScratchDirectory.Create("uninstall-silent-data");

        // ⚠️ THE CLIENT'S CONFIGURATION IS SCRATCH, and since 2026-09-16 that is
        // a requirement rather than tidiness. HookRegistration.Run has no seam
        // over what is registered already, and the registrar now reads it on
        // EVERY intent -- so without this the hook asks about the maintainer's
        // own ~/.claude.json, finds an entry foreign to this scratch install
        // root, and refuses. The read is the product's; the file it reads is
        // this arm's.
        using var clientConfig = ScratchDirectory.Create("uninstall-silent-data-config");
        using var pointed = PointTheClientAt(clientConfig.Path);

        var paths = new LocalAppDataPaths(data.Path);
        var planted = PlantADataRoot(paths);
        var asked = 0;

        var outcome = HookRegistration.Run(
            RegistrationIntent.Uninstall,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeClientCommandLine(),
            paths,
            silent: true,
            ask: _ =>
            {
                asked++;
                return true;
            });

        // Nothing was registered with the double first, so the registration half
        // reports that there was nothing to remove -- which is still what was
        // asked for, and is the half this arm does not judge.
        await Assert.That(outcome.Registration.IsWhatWasAskedFor).IsTrue();
        await Assert.That(asked).IsEqualTo(0);

        await Assert.That(outcome.Disposal).IsNotNull();
        await Assert.That(outcome.Disposal!.Choice).IsEqualTo(DataRootChoice.Keep);
        await Assert.That(outcome.Disposal.Asked).IsFalse();

        await Assert.That(File.Exists(planted)).IsTrue();
        await Assert.That(Directory.Exists(paths.BrowsersDirectory)).IsTrue();
    }

    /// <summary>
    /// The prompt's default is <i>keep</i>: an answer of no leaves everything
    /// exactly where it was.
    /// </summary>
    /// <remarks>
    /// <b>The question is asserted as well as the answer</b>, because a prompt
    /// that did not name the directory or say what it holds is a prompt answered
    /// by habit. The size is the whole of what makes it a real question: saying
    /// yes means a ~768 MB download next time.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallAnsweredNoKeepsTheDataRootAndTheQuestionNamedIt()
    {
        using var install = ScratchDirectory.Create("uninstall-keep");
        using var data = ScratchDirectory.Create("uninstall-keep-data");

        // ⚠️ THE CLIENT'S CONFIGURATION IS SCRATCH, and since 2026-09-16 that is
        // a requirement rather than tidiness. HookRegistration.Run has no seam
        // over what is registered already, and the registrar now reads it on
        // EVERY intent -- so without this the hook asks about the maintainer's
        // own ~/.claude.json, finds an entry foreign to this scratch install
        // root, and refuses. The read is the product's; the file it reads is
        // this arm's.
        using var clientConfig = ScratchDirectory.Create("uninstall-keep-data-config");
        using var pointed = PointTheClientAt(clientConfig.Path);

        var paths = new LocalAppDataPaths(data.Path);
        var planted = PlantADataRoot(paths);
        var questions = new List<string>();

        var outcome = HookRegistration.Run(
            RegistrationIntent.Uninstall,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeClientCommandLine(),
            paths,
            silent: false,
            ask: message =>
            {
                questions.Add(message);
                return false;
            });

        await Assert.That(questions.Count).IsEqualTo(1);
        await Assert.That(questions[0]).Contains(data.Path);
        await Assert.That(questions[0]).Contains("KB");
        await Assert.That(questions[0]).Contains("Delete it as well?");

        await Assert.That(outcome.Disposal!.Choice).IsEqualTo(DataRootChoice.Keep);
        await Assert.That(outcome.Disposal.Asked).IsTrue();
        await Assert.That(outcome.Disposal.Bytes).IsGreaterThan(0);

        await Assert.That(File.Exists(planted)).IsTrue();
    }

    /// <summary>
    /// An answer of yes removes the data root, through <c>TreeDelete</c>, with
    /// nothing left behind -- the hook's own open log included.
    /// </summary>
    /// <remarks>
    /// <b>The log is the interesting node and it is why the removal is split
    /// from the decision.</b> The hook writes its process log inside the data
    /// root, so a delete performed while that file was open would leave exactly
    /// one directory standing -- the one holding the record of the decision. The
    /// product closes the log first; this asserts the consequence rather than
    /// the arrangement.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallAnsweredYesRemovesTheWholeDataRootIncludingTheHooksOwnLog()
    {
        using var install = ScratchDirectory.Create("uninstall-remove");
        using var data = ScratchDirectory.Create("uninstall-remove-data");

        // ⚠️ THE CLIENT'S CONFIGURATION IS SCRATCH, and since 2026-09-16 that is
        // a requirement rather than tidiness. HookRegistration.Run has no seam
        // over what is registered already, and the registrar now reads it on
        // EVERY intent -- so without this the hook asks about the maintainer's
        // own ~/.claude.json, finds an entry foreign to this scratch install
        // root, and refuses. The read is the product's; the file it reads is
        // this arm's.
        using var clientConfig = ScratchDirectory.Create("uninstall-remove-data-config");
        using var pointed = PointTheClientAt(clientConfig.Path);

        var paths = new LocalAppDataPaths(data.Path);
        var planted = PlantADataRoot(paths);

        var outcome = HookRegistration.Run(
            RegistrationIntent.Uninstall,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeClientCommandLine(),
            paths,
            silent: false,
            ask: _ => true);

        await Assert.That(outcome.Registration.IsWhatWasAskedFor).IsTrue();
        await Assert.That(outcome.Disposal!.Choice).IsEqualTo(DataRootChoice.Remove);

        await Assert.That(string.Join(Environment.NewLine, outcome.Disposal.Failures)).IsEmpty();
        await Assert.That(File.Exists(planted)).IsFalse();
        await Assert.That(Directory.Exists(paths.LogDirectory)).IsFalse();
        await Assert.That(Directory.Exists(data.Path)).IsFalse();
    }

    /// <summary>
    /// An update never asks about the data root and never touches it -- which is
    /// the founding promise of separating the two directories at all.
    /// </summary>
    /// <remarks>
    /// <b>By construction rather than by care, and this is what holds the
    /// construction.</b> Only <see cref="RegistrationIntent.Uninstall"/> reaches
    /// the disposal; an install and an update come back with no disposal report
    /// at all, so there is no path on which a wrong answer could delete
    /// somebody's browsers mid-upgrade.
    /// </remarks>
    /// <param name="intent">The lifecycle event that is not an uninstall.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(RegistrationIntent.Install)]
    [Arguments(RegistrationIntent.Update)]
    public async Task AnUpgradeNeverAsksAboutTheDataRootAndNeverTouchesIt(RegistrationIntent intent)
    {
        using var install = ScratchDirectory.Create("upgrade-keeps");
        using var data = ScratchDirectory.Create("upgrade-keeps-data");

        // ⚠️ THE CLIENT'S CONFIGURATION IS SCRATCH, and since 2026-09-16 that is
        // a requirement rather than tidiness. HookRegistration.Run has no seam
        // over what is registered already, and the registrar now reads it on
        // EVERY intent -- so without this the hook asks about the maintainer's
        // own ~/.claude.json, finds an entry foreign to this scratch install
        // root, and refuses. The read is the product's; the file it reads is
        // this arm's.
        using var clientConfig = ScratchDirectory.Create("upgrade-keeps-data-config");
        using var pointed = PointTheClientAt(clientConfig.Path);

        var paths = new LocalAppDataPaths(data.Path);
        var planted = PlantADataRoot(paths);
        var asked = 0;

        var outcome = HookRegistration.Run(
            intent,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeClientCommandLine(),
            paths,
            silent: false,
            ask: _ =>
            {
                asked++;
                return true;
            });

        await Assert.That(outcome.Disposal).IsNull();
        await Assert.That(asked).IsEqualTo(0);
        await Assert.That(File.Exists(planted)).IsTrue();
        await Assert.That(Directory.Exists(paths.BrowsersDirectory)).IsTrue();
    }

    /// <summary>
    /// A data root holding nothing but this hook's own log is not worth a
    /// question.
    /// </summary>
    /// <remarks>
    /// <b>The install hook creates the data root itself</b> -- it opens a log
    /// there -- so by the time an uninstall runs, the directory always exists.
    /// Asking about a few kilobytes of log and a registration record, which are
    /// the two files somebody reads <i>after</i> an uninstall, would be a modal
    /// window with nothing in it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallOfABrowserAiThatNeverRanAsksNothing()
    {
        using var install = ScratchDirectory.Create("uninstall-unused");
        using var data = ScratchDirectory.Create("uninstall-unused-data");

        // ⚠️ THE CLIENT'S CONFIGURATION IS SCRATCH, and since 2026-09-16 that is
        // a requirement rather than tidiness. HookRegistration.Run has no seam
        // over what is registered already, and the registrar now reads it on
        // EVERY intent -- so without this the hook asks about the maintainer's
        // own ~/.claude.json, finds an entry foreign to this scratch install
        // root, and refuses. The read is the product's; the file it reads is
        // this arm's.
        using var clientConfig = ScratchDirectory.Create("uninstall-unused-data-config");
        using var pointed = PointTheClientAt(clientConfig.Path);

        var paths = new LocalAppDataPaths(data.Path);
        var asked = 0;

        var outcome = HookRegistration.Run(
            RegistrationIntent.Uninstall,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeClientCommandLine(),
            paths,
            silent: false,
            ask: _ =>
            {
                asked++;
                return true;
            });

        await Assert.That(asked).IsEqualTo(0);
        await Assert.That(outcome.Disposal!.Choice).IsEqualTo(DataRootChoice.Keep);
        await Assert.That(outcome.Disposal.Asked).IsFalse();

        // The record survives, which is the point of not asking: it is what a
        // person reads when a client still shows BrowserAI after an uninstall.
        await Assert.That(File.Exists(RegistrationRecord.PathFor(data.Path))).IsTrue();
    }

    /// <summary>
    /// Silence is read off the parent's command line, by token, and an unknown
    /// parent is silent.
    /// </summary>
    /// <remarks>
    /// <b>Velopack passes the hook nothing</b> -- not a flag, not an environment
    /// variable -- so <c>Update.exe</c>'s own command line is the only place the
    /// answer exists. Windows keeps the two shapes apart in the registry:
    /// <c>UninstallString</c> is <c>Update.exe --uninstall</c> and
    /// <c>QuietUninstallString</c> is the same with <c>--silent</c>. The last two
    /// arguments are the controls that matter: a path containing <c>-s</c> is not
    /// the flag, and an unreadable parent must read as silent, because the
    /// consequence of the other answer is a modal window nobody is there to
    /// close.
    /// </remarks>
    /// <param name="commandLine">What the parent was started with.</param>
    /// <param name="silent">What that means.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(@"""C:\Users\x\AppData\Local\BrowserAI.app\Update.exe"" --uninstall", false)]
    [Arguments(@"""C:\Users\x\AppData\Local\BrowserAI.app\Update.exe"" --uninstall --silent", true)]
    [Arguments(@"""C:\Users\x\AppData\Local\BrowserAI.app\Update.exe"" --uninstall -s", true)]
    [Arguments(@"""C:\Users\x\AppData\Local\BrowserAI.app\Update.exe"" --uninstall --SILENT", true)]
    [Arguments(@"C:\tools\update-server\Update.exe --uninstall", false)]
    [Arguments(null, true)]
    public async Task SilenceIsReadOffTheParentsCommandLineByToken(string? commandLine, bool silent) =>
        await Assert.That(DataRootDisposal.IsSilent(commandLine)).IsEqualTo(silent);

    /// <summary>
    /// Plants what a used BrowserAI leaves behind: a browsers tree, an index and
    /// a file to watch.
    /// </summary>
    /// <param name="paths">The scratch data seam.</param>
    /// <returns>The planted file's path.</returns>
    private static string PlantADataRoot(LocalAppDataPaths paths)
    {
        _ = Directory.CreateDirectory(paths.BrowsersDirectory);
        _ = Directory.CreateDirectory(paths.IndexDirectory);

        var planted = Path.Combine(paths.BrowsersDirectory, "chromium-0000", "chrome.exe");

        _ = Directory.CreateDirectory(Path.GetDirectoryName(planted)!);
        File.WriteAllText(planted, "not really a browser, but it is 768 MB in spirit");

        return planted;
    }

    // ---- The real client ----------------------------------------------------

    /// <summary>
    /// The scratch configuration a real-client arm hands over already carries
    /// what the client reads as "onboarding is finished".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The behavioural half of the onboarding guard.</b>
    /// <see cref="HouseRuleTests.EveryScratchClientConfigurationIsSeededAsOnboardedBeforeTheClientRuns"/>
    /// holds that every site goes through the seam; this holds that the seam
    /// writes the file the client would read, in the directory the client would
    /// read it from, <b>before</b> the scope is anything a child could inherit.
    /// It runs on every build and needs no client, because what it asserts is a
    /// file on disk rather than a behaviour of somebody else's binary.
    /// </para>
    /// <para>
    /// ⚠️ <b>Asserted, not measured against the flow.</b> Nothing here shows that
    /// an unseeded directory would have opened a sign-in window -- establishing
    /// that means running the flow on the maintainer's desktop, which is the
    /// event being guarded against. The reasoning, the bundle source it was read
    /// out of and the absence of any non-interactive signal to set instead are on
    /// <see cref="OnboardedClientConfig"/>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheScratchConfigurationIsSeededWithWhatTheClientReadsAsOnboarded()
    {
        using var config = ScratchDirectory.Create("registration-onboarded");

        // A fresh scratch directory is empty, which is the state being guarded
        // against: to the client that is a machine nobody has ever signed in on.
        await Assert.That(Directory.EnumerateFileSystemEntries(config.Path)).IsEmpty();

        using (PointTheClientAt(config.Path))
        {
            var seeded = Path.Combine(config.Path, OnboardedClientConfig.FileName);

            await Assert.That(File.Exists(seeded)).IsTrue();

            var written = await File.ReadAllTextAsync(seeded);

            await Assert.That(written).Contains($"\"{OnboardedClientConfig.Marker}\":true");

            // The client prefers `.config.json` in the same directory when it
            // exists, and a scratch directory holds none -- so the file above is
            // the one it resolves. Read out of the bundle, quoted on
            // OnboardedClientConfig.
            await Assert.That(File.Exists(Path.Combine(config.Path, ".config.json"))).IsFalse();

            // The variable really is pointing at that directory while the scope
            // is open, which is what makes the seeding reachable at all.
            await Assert.That(Environment.GetEnvironmentVariable(ConfigDirectoryVariable))
                .IsEqualTo(config.Path);
        }

        // And the file name the rest of this class uses is the same one.
        await Assert.That(ConfigFileName).IsEqualTo(OnboardedClientConfig.FileName);
    }

    /// <summary>
    /// The real client still says what its exit codes cannot.
    /// </summary>
    /// <remarks>
    /// <b>The one assertion the double cannot make.</b> Every failure the client
    /// has exits 1 -- a duplicate <c>add</c> and a broken configuration are
    /// indistinguishable by exit code -- so the product discriminates on
    /// upstream's English. This is what turns a wording change into a red test
    /// instead of a registration silently reported as failed in the field.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClientStillSaysWhatTheExitCodesCannot()
    {
        var client = SuiteEnvironment.RequireClientCommandLine();

        using var config = ScratchDirectory.Create("registration-wording");
        using var install = ScratchDirectory.Create("registration-wording-install");

        var command = InstalledLayout.Create(install.Path);
        var commands = new ClientCommandLine();

        using (PointTheClientAt(config.Path))
        {
            var first = commands.Run(client, McpClientRegistration.AddArguments(command), McpClientRegistration.Budget);
            await Assert.That(first.Succeeded).IsTrue();

            var duplicate = commands.Run(client, McpClientRegistration.AddArguments(command), McpClientRegistration.Budget);

            await Assert.That(duplicate.Succeeded).IsFalse();
            await Assert.That(McpClientRegistration.MeansAlreadyRegistered(duplicate.ExitCode, duplicate.Output)).IsTrue();

            var removed = commands.Run(client, McpClientRegistration.RemoveArguments(), McpClientRegistration.Budget);
            await Assert.That(removed.Succeeded).IsTrue();

            var absent = commands.Run(client, McpClientRegistration.RemoveArguments(), McpClientRegistration.Budget);

            await Assert.That(absent.Succeeded).IsFalse();
            await Assert.That(McpClientRegistration.MeansNothingToRemove(absent.ExitCode, absent.Output)).IsTrue();

            // And the two are told apart, which is the property that matters:
            // an "already exists" is not read as "nothing to remove" or the
            // reverse.
            await Assert.That(McpClientRegistration.MeansNothingToRemove(duplicate.ExitCode, duplicate.Output)).IsFalse();
            await Assert.That(McpClientRegistration.MeansAlreadyRegistered(absent.ExitCode, absent.Output)).IsFalse();
        }
    }

    /// <summary>
    /// The whole mechanism against the real client: registered at user scope,
    /// idempotent, removable -- and the maintainer's own configuration untouched.
    /// </summary>
    /// <remarks>
    /// <b>This is the proof that the charter's promise is kept</b> -- one
    /// registration, at user scope, available in every repository, with no file
    /// written into any of them. The entry is asserted in the client's own
    /// configuration file rather than in the client's report of it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRealClientRegistersBrowserAiAtUserScopeAndNothingElseIsTouched()
    {
        _ = SuiteEnvironment.RequireClientCommandLine();

        using var config = ScratchDirectory.Create("registration-live");
        using var install = ScratchDirectory.Create("registration-live-install");

        var command = InstalledLayout.Create(install.Path);
        var commands = new ClientCommandLine();
        var (logger, _) = Capture();
        var file = Path.Combine(config.Path, ConfigFileName);

        using (PointTheClientAt(config.Path))
        {
            var installed = McpRegistrar.Apply(RegistrationIntent.Install, command, commands, logger);

            await Assert.That(installed.Status).IsEqualTo(RegistrationStatus.Registered);
            await Assert.That(File.Exists(file)).IsTrue();

            var written = await File.ReadAllTextAsync(file);

            await Assert.That(written).Contains($"\"{McpClientRegistration.ServerName}\"");

            // The sibling server, JSON-escaped, and never the app that composed it.
            var registered = InstalledLayout.ServerIn(install.Path);

            await Assert.That(written).Contains(registered.Replace(@"\", @"\\", StringComparison.Ordinal));
            await Assert.That(written).DoesNotContain(
                command.Replace(@"\", @"\\", StringComparison.Ordinal) + "\"");

            // Idempotence, against the client rather than against the double.
            await Assert.That(McpRegistrar.Apply(RegistrationIntent.Update, command, commands, logger).Status)
                .IsEqualTo(RegistrationStatus.AlreadyRegistered);
            await Assert.That(McpRegistrar.Apply(RegistrationIntent.Install, command, commands, logger).Status)
                .IsEqualTo(RegistrationStatus.Registered);

            var afterFour = await File.ReadAllTextAsync(file);

            await Assert.That(Occurrences(afterFour, $"\"{McpClientRegistration.ServerName}\"")).IsEqualTo(1);

            var removed = McpRegistrar.Apply(RegistrationIntent.Uninstall, command, commands, logger);

            await Assert.That(removed.Status).IsEqualTo(RegistrationStatus.Unregistered);

            var afterRemoval = await File.ReadAllTextAsync(file);

            await Assert.That(afterRemoval).DoesNotContain($"\"{McpClientRegistration.ServerName}\"");

            await Assert.That(McpRegistrar.Apply(RegistrationIntent.Uninstall, command, commands, logger).Status)
                .IsEqualTo(RegistrationStatus.NothingToUnregister);
        }

        // ⚠️ The negative that matters. The registered path carries this run's
        // GUID, so its absence from the user's own configuration is proof rather
        // than an argument -- and it survives the client rewriting that file for
        // its own reasons while the test runs, which a hash comparison would not.
        var mine = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
            ConfigFileName);

        if (File.Exists(mine))
        {
            var untouched = await File.ReadAllTextAsync(mine);

            await Assert.That(untouched).DoesNotContain(install.Path.Replace(@"\", @"\\", StringComparison.Ordinal));
            await Assert.That(untouched).DoesNotContain(install.Path);
        }
    }

    /// <summary>
    /// The product finds the real client the way it says it does.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClientIsLocatedByFileNameAndNeverAsAShim()
    {
        var located = SuiteEnvironment.RequireClientCommandLine();

        await Assert.That(Path.IsPathFullyQualified(located)).IsTrue();
        await Assert.That(Path.GetFileName(located)).IsEqualTo(McpClientRegistration.ClientExecutable);
        await Assert.That(File.Exists(located)).IsTrue();

        // A .cmd shim cannot be started without cmd.exe, which stack.md
        // deviation 1 forbids -- so the name searched for carries its extension
        // and a shim can never be found by it.
        await Assert.That(McpClientRegistration.ClientExecutable).EndsWith(".exe");

        await Assert.That(new ClientCommandLine().Locate("browserai-no-such-client.exe")).IsNull();
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>
    /// The verbs a refused update followed by an install produces: the update
    /// runs nothing at all, and the install removes before it adds.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Replaces <c>AddRemoveAdd</c>, 2026-09-15</b> (previously
    /// <c>["add", "remove", "add"]</c>, described as "an update never removes,
    /// an install always does"). The update in that arm meets a registration
    /// that is not under this install root, and since this day that is reported
    /// rather than silently accepted, so the update runs no verb at all. What is
    /// unchanged is the property the arm asserts: an update does not overwrite.
    /// </remarks>
    private static readonly string[] RemoveAdd = ["remove", "add"];

    /// <summary>
    /// What a <see cref="FakeClientCommandLine"/> currently holds, as the view
    /// the registrar would otherwise read off the real client's file.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Every arm driving <see cref="McpRegistrar.Apply"/> through a double
    /// has to pass this -- every intent, not just
    /// <see cref="RegistrationIntent.Update"/>.</b> <i>Widened 2026-09-16
    /// (previously "Every arm driving <c>RegistrationIntent.Update</c> through a
    /// double")</i>: until that day only an update read the client's file, so an
    /// install or an uninstall without the seam happened to be deterministic. It
    /// is not any more, and the six arms that relied on it went red the moment
    /// the read moved -- against the maintainer's own registration, which is
    /// foreign to every scratch install root. The registrar's default reads the
    /// client's own user-scope configuration, which on this machine is the
    /// maintainer's, so an arm without the seam asks a question about whatever
    /// happens to be registered there and answers it differently on a different
    /// machine. Nothing enforces this; the failure it prevents is a red that
    /// moves with the environment, which is the worst kind to diagnose.
    /// </remarks>
    private static Func<string, RegistrationView> WhatTheDoubleHolds(FakeClientCommandLine client) =>
        root =>
        {
            var command = client.Registered.GetValueOrDefault(McpClientRegistration.ServerName);

            return new RegistrationView(
                RegistrationScope.User,
                "<the double>",
                command,
                McpRegistryView.Classify(command, root),
                null);
        };

    /// <summary>
    /// Reads a log file the way everything else in this suite does -- sharing
    /// with a writer that may still hold it.
    /// </summary>
    /// <remarks>
    /// The process log is machine-wide, so any BrowserAI on this machine may
    /// have it open; <c>File.ReadAllText</c> asks for <c>FileShare.Read</c>,
    /// which denies write to an existing writer and is refused.
    /// </remarks>
    /// <param name="path">The log file.</param>
    /// <returns>Its text.</returns>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static (ILogger Logger, CapturingLoggerProvider Log) Capture()
    {
        var provider = new CapturingLoggerProvider();
        return (provider.CreateLogger("BrowserAI.Registration"), provider);
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var at = text.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>
    /// Points the client at a scratch configuration directory for the life of
    /// the returned scope.
    /// </summary>
    /// <remarks>
    /// <b>Process-wide, because that is the only channel there is.</b> The child
    /// inherits this process's environment block, and the alternative -- an
    /// environment overlay on the product's own command runner -- would be a seam
    /// that exists for no reason but this test. It is restored however the test
    /// ends, and the whole class is <c>[NotInParallel]</c> with no key --
    /// <i>corrected 2026-09-15 (previously "The arms that use it are
    /// <c>[NotInParallel]</c> on one group so they cannot overwrite each other's
    /// value")</i>, because overwriting each other's value was never the only
    /// way this goes wrong: any child of any arm reads it too.
    /// </remarks>
    /// <remarks>
    /// ⚠️ <b>The directory is SEEDED as already onboarded on the way through, and
    /// that is what makes the seeding inseparable from the use</b> -- an empty
    /// configuration directory is, to the real client, a machine nobody has ever
    /// signed in on. See <see cref="OnboardedClientConfig"/> for what is written
    /// and the bundle source it was read out of, and
    /// <see cref="HouseRuleTests.EveryScratchClientConfigurationIsSeededAsOnboardedBeforeTheClientRuns"/>
    /// for the scan that refuses a site which skips it.
    /// </remarks>
    /// <param name="directory">The scratch configuration directory.</param>
    /// <returns>The scope that restores whatever was there before.</returns>
    private static EnvironmentScope PointTheClientAt(string directory) => new(ConfigDirectoryVariable, OnboardedClientConfig.Seed(directory));
}

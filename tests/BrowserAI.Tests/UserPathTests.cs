// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Win32;

namespace BrowserAI.Tests;

/// <summary>
/// Q294 b: a Codex project entry names <c>BrowserAI.Server.exe</c> alone, the install
/// puts its own folder on the user's PATH, and the Codex ownership check resolves the
/// bare name the way Codex does.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The maintainer's decision, 2026-09-24, verbatim: <i>"Q294 b"</i>.</b> Codex
/// expands no variable in a server's command -- 0 of 48 across four spellings,
/// measured, and read in its launcher -- so no spelling of an install's path resolves
/// on another machine; a bare name does, through the PATH Codex hands the server.
/// </para>
/// <para>
/// <b>Nothing here writes the person's own PATH.</b> The hook body takes its store as a
/// required argument and these arms hand it <see cref="ScratchUserPath"/>, or a
/// registry store over a scratch key under <c>HKCU\Software</c> that the arm deletes.
/// The arms that run a real <c>Setup.exe</c> are in <c>RealInstallerTests</c>.
/// </para>
/// <para>
/// <b>Planted red 2026-09-24</b>, each against the behaviour before the change: the
/// hook with no PATH step, a store that wrote every value as <c>REG_SZ</c>, the Codex
/// classifier with no bare-name branch, and a Codex project command that was the
/// absolute path.
/// </para>
/// </remarks>
internal sealed class UserPathTests
{
    /// <summary>Two entries the person already had, as a user PATH carries them.</summary>
    private const string Theirs = @"C:\Tools;%USERPROFILE%\bin";

    /// <summary>
    /// The install hook puts its own folder on the PATH once, the uninstall hook takes
    /// exactly that entry off, and another install root's entry is never touched.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachInstallRootPutsItsOwnFolderOnThePathAndTakesOnlyThatOff()
    {
        using var first = ScratchDirectory.Create("user-path-root-a");
        using var second = ScratchDirectory.Create("user-path-root-b");
        using var data = ScratchDirectory.Create("user-path-data");

        var appA = InstalledLayout.Create(first.Path);
        var appB = InstalledLayout.Create(second.Path);
        var entryA = Path.GetDirectoryName(InstalledLayout.ServerIn(first.Path))!;
        var entryB = Path.GetDirectoryName(InstalledLayout.ServerIn(second.Path))!;

        var store = new ScratchUserPath { Value = new UserPathValue(Theirs, RegistryValueKind.ExpandString) };

        // ---- Install A: appended after a separator, kind kept, announced once.
        var installed = Hook(RegistrationIntent.Install, appA, store, data.Path);

        await Assert.That(installed?.Change).IsEqualTo(UserPathChange.Added);
        await Assert.That(store.Value).IsEqualTo(new UserPathValue($"{Theirs};{entryA}", RegistryValueKind.ExpandString));
        await Assert.That(store.Announcements).IsEqualTo(1);

        // ---- An update over the same root writes nothing and announces nothing.
        var updated = Hook(RegistrationIntent.Update, appA, store, data.Path);

        await Assert.That(updated?.Change).IsEqualTo(UserPathChange.AlreadyThere);
        await Assert.That(store.Writes).IsEqualTo(1);
        await Assert.That(store.Announcements).IsEqualTo(1);

        // ---- Install B beside it: its own entry, and A's untouched.
        _ = Hook(RegistrationIntent.Install, appB, store, data.Path);

        await Assert.That(store.Value!.Text).IsEqualTo($"{Theirs};{entryA};{entryB}");

        // ---- Uninstall A: A's entry goes, B's and the person's stay.
        var removed = Hook(RegistrationIntent.Uninstall, appA, store, data.Path);

        await Assert.That(removed?.Change).IsEqualTo(UserPathChange.Removed);
        await Assert.That(store.Value!.Text).IsEqualTo($"{Theirs};{entryB}");

        // ---- Uninstall B: the value is what it was before either install, byte for byte.
        _ = Hook(RegistrationIntent.Uninstall, appB, store, data.Path);

        await Assert.That(store.Value).IsEqualTo(new UserPathValue(Theirs, RegistryValueKind.ExpandString));

        // ---- An entry the person wrote in another spelling is not the install's to take.
        var spelled = new ScratchUserPath { Value = new UserPathValue($@"{Theirs};%LOCALAPPDATA%\elsewhere\current", RegistryValueKind.ExpandString) };

        await Assert.That(Hook(RegistrationIntent.Uninstall, appA, spelled, data.Path)?.Change).IsEqualTo(UserPathChange.NotThere);
        await Assert.That(spelled.Writes).IsEqualTo(0);
    }

    /// <summary>
    /// A value that ends in a separator, an empty value and no value at all each come
    /// back exactly as they were after an install and an uninstall.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstallAndItsUninstallLeaveThePathByteForByteAsTheyFoundIt()
    {
        const string Entry = @"C:\Users\someone\AppData\Local\BrowserAI.app\current";

        foreach (var before in new UserPathValue?[]
        {
            new(@"C:\Tools;", RegistryValueKind.ExpandString),
            new(@"C:\Tools", RegistryValueKind.String),
            new(string.Empty, RegistryValueKind.ExpandString),
            null,
        })
        {
            var store = new ScratchUserPath { Value = before };

            await Assert.That(UserPath.Add(store, Entry).Change).IsEqualTo(UserPathChange.Added);
            await Assert.That(UserPath.Segments(store.Value!.Text)).Contains(Entry);

            await Assert.That(UserPath.Remove(store, Entry).Change).IsEqualTo(UserPathChange.Removed);
            await Assert.That(store.Value).IsEqualTo(before);
        }

        // And a value created from nothing is the kind Windows gives a user PATH.
        var created = new ScratchUserPath();

        _ = UserPath.Add(created, Entry);

        await Assert.That(created.Value).IsEqualTo(new UserPathValue(Entry, RegistryValueKind.ExpandString));
    }

    /// <summary>
    /// The registry store keeps a value's kind and hands its text back unexpanded.
    /// </summary>
    /// <remarks>
    /// <b>Over a scratch key under <c>HKCU\Software</c></b>, deleted when the arm ends,
    /// because the property is the registry's and a store kept in memory cannot have it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRegistryStoreKeepsTheKindAndTheUnexpandedText()
    {
        var subKey = $@"Software\BrowserAI.Tests\UserPath-{Guid.NewGuid():N}";
        var store = new RegistryUserPathStore(Registry.CurrentUser, subKey, announce: false);
        const string Entry = @"C:\Users\someone\AppData\Local\BrowserAI.app\current";

        try
        {
            foreach (var before in new[]
            {
                new UserPathValue(@"%SystemRoot%\x;C:\Tools", RegistryValueKind.ExpandString),
                new UserPathValue(@"C:\Tools;", RegistryValueKind.String),
            })
            {
                store.Write(before);

                await Assert.That(store.Read()).IsEqualTo(before);

                _ = UserPath.Add(store, Entry);

                await Assert.That(store.Read()?.Kind).IsEqualTo(before.Kind);
                await Assert.That(store.Read()?.Text).IsEqualTo($"{before.Text};{Entry}");

                _ = UserPath.Remove(store, Entry);

                await Assert.That(store.Read()).IsEqualTo(before);
            }

            store.Delete();

            _ = UserPath.Add(store, Entry);

            await Assert.That(store.Read()).IsEqualTo(new UserPathValue(Entry, RegistryValueKind.ExpandString));

            _ = UserPath.Remove(store, Entry);

            await Assert.That(store.Read()).IsNull();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
    }

    /// <summary>
    /// A bare name in a Codex entry is judged by the file it finds first on a PATH:
    /// this install is ours, another is not, and nothing found is the product's own
    /// spelling gone stale.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABareNameIsJudgedByTheFileItFindsFirstOnThePath()
    {
        using var ours = ScratchDirectory.Create("bare-name-ours");
        using var theirs = ScratchDirectory.Create("bare-name-theirs");
        using var empty = ScratchDirectory.Create("bare-name-empty");

        _ = InstalledLayout.Create(ours.Path);
        _ = InstalledLayout.Create(theirs.Path);

        var oursFolder = Path.GetDirectoryName(InstalledLayout.ServerIn(ours.Path))!;
        var theirsFolder = Path.GetDirectoryName(InstalledLayout.ServerIn(theirs.Path))!;
        const string Name = RegistrationTarget.ServerFileName;

        // Found under this install, after a folder that holds nothing.
        await Assert.That(CodexRegistryView.Classify(Name, ours.Path, () => [empty.Path, oursFolder]))
            .IsEqualTo(RegistrationOwnership.OursAndPresent);

        // Another install first on the PATH: the name finds that one, which is not ours.
        await Assert.That(CodexRegistryView.Classify(Name, ours.Path, () => [theirsFolder, oursFolder]))
            .IsEqualTo(RegistrationOwnership.Foreign);

        // Nothing on the PATH: the product's own spelling, pointing at nothing.
        await Assert.That(CodexRegistryView.Classify(Name, ours.Path, () => [empty.Path]))
            .IsEqualTo(RegistrationOwnership.OursAndStale);

        // Any other bare name that finds nothing is somebody else's.
        await Assert.That(CodexRegistryView.Classify("node.exe", ours.Path, () => [empty.Path]))
            .IsEqualTo(RegistrationOwnership.Foreign);

        // A relative path is not a bare name and is not ours.
        await Assert.That(CodexRegistryView.Classify(@"current\" + Name, ours.Path, () => [oursFolder]))
            .IsEqualTo(RegistrationOwnership.Foreign);

        // And an absolute path is judged as it always was, with no PATH read at all.
        await Assert.That(CodexRegistryView.Classify(InstalledLayout.ServerIn(ours.Path), ours.Path, () => throw new InvalidOperationException("no PATH read for an absolute path")))
            .IsEqualTo(RegistrationOwnership.OursAndPresent);
    }

    /// <summary>
    /// A Codex project entry names the server alone, whatever the PATH finds, and the
    /// sentence after it says what the name finds; Claude Code's is unchanged.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACodexProjectEntryNamesTheServerAloneAndSaysWhatItFinds()
    {
        const string Server = @"C:\Users\someone\AppData\Local\BrowserAI.app\current\BrowserAI.Server.exe";
        const string Other = @"D:\elsewhere\current\BrowserAI.Server.exe";

        var here = CodexRegistration.ProjectCommandGiven(Server, Server);
        var elsewhere = CodexRegistration.ProjectCommandGiven(Server, Other);
        var nowhere = CodexRegistration.ProjectCommandGiven(Server, null);

        // Never an absolute path, in any of the three.
        foreach (var project in new[] { here, elsewhere, nowhere })
        {
            await Assert.That(project.Command).IsEqualTo(RegistrationTarget.ServerFileName);
            await Assert.That(project.Note).IsNotNull();
        }

        await Assert.That(here.Note!).Contains("finds this install");
        await Assert.That(here.Note!).Contains("restarted");
        await Assert.That(elsewhere.Note!).Contains(Other);
        await Assert.That(nowhere.Note!).Contains("No folder on your PATH holds one yet");

        // The client's own member answers the same way, from this machine's PATH.
        await Assert.That(RegistrationClient.Codex.ProjectCommandFor(Server, Path.GetDirectoryName(Path.GetDirectoryName(Server))).Command)
            .IsEqualTo(RegistrationTarget.ServerFileName);

        // Claude Code: the portable spelling at the default location, the absolute
        // path and the reason anywhere else.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var defaultServer = Path.Combine(local, "BrowserAI.app", RegistrationTarget.CurrentDirectoryName, RegistrationTarget.ServerFileName);

        var portable = RegistrationClient.ClaudeCode.ProjectCommandFor(defaultServer, Path.Combine(local, "BrowserAI.app"));

        await Assert.That(portable.Command).StartsWith("${LOCALAPPDATA}/");
        await Assert.That(portable.Note).IsNull();

        var moved = RegistrationClient.ClaudeCode.ProjectCommandFor(Other, @"D:\elsewhere");

        await Assert.That(moved.Command).IsEqualTo(Other);
        await Assert.That(moved.Note!).Contains("not at its default location");
    }

    /// <summary>One hook pass, the way the hook runs it, against a scratch PATH.</summary>
    private static UserPathReport? Hook(RegistrationIntent intent, string image, ScratchUserPath store, string data) =>
        HookRegistration.Run(
            intent,
            "9.9.9",
            image,
            new FakeClientCommandLine(),
            new LocalAppDataPaths(data),
            store,
            silent: true,
            ask: _ => false,
            clients: [RegistrationClient.ClaudeCode]).PathEntry;
}

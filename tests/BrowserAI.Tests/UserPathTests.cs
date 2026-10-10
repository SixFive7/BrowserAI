// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Win32;

namespace BrowserAI.Tests;

/// <summary>
/// Q294 b: a Codex project entry names <c>BrowserAI.Server.exe</c> alone, and the
/// install puts its own folder on the user's PATH.
/// </summary>
/// <remarks>
/// <i>Corrected 2026-10-03 (previously "and the Codex ownership check resolves the
/// bare name the way Codex does")</i>: that check went to RegisterAI with the switch,
/// and its arm with it; RegisterAI reports what the name finds as <c>resolvesTo</c>.
/// </remarks>
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
    /// A project entry names the server alone for both clients, whatever the PATH finds,
    /// and the sentence after it says what the name finds.
    /// </summary>
    /// <remarks>
    /// <b>Claude Code too since 2026-10-10</b>, the maintainer's 30 that day: its entry took
    /// the bare name, and its sentence is Codex's in its own words. <b>Planted red
    /// 2026-10-10</b> against Claude Code's <c>${LOCALAPPDATA}</c> spelling and its missing
    /// sentence. <i>Previously <c>ACodexProjectEntryNamesTheServerAloneAndSaysWhatItFinds</c>,
    /// which held Claude Code's spelling unchanged.</i>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProjectEntryNamesTheServerAloneForBothClientsAndSaysWhatItFinds()
    {
        const string Server = @"C:\Users\someone\AppData\Local\BrowserAI.app\current\BrowserAI.exe";
        const string Other = @"D:\elsewhere\current\BrowserAI.exe";

        // Never an absolute path, wherever the install is.
        await Assert.That(RegistrationClient.Codex.ProjectCommandFor(Server, Path.GetDirectoryName(Path.GetDirectoryName(Server))).Command)
            .IsEqualTo(RegistrationTarget.AppFileName);
        await Assert.That(RegistrationClient.Codex.ProjectCommandFor(Other, @"D:\elsewhere").Command)
            .IsEqualTo(RegistrationTarget.AppFileName);

        // The sentence after it, from what RegisterAI says the name finds.
        var here = RegistrationClient.Codex.ProjectNoteAfter(Server, Server);
        var elsewhere = RegistrationClient.Codex.ProjectNoteAfter(Server, Other);
        var nowhere = RegistrationClient.Codex.ProjectNoteAfter(Server, null);

        await Assert.That(here!).Contains("finds this install");

        // ⚠️ Without the restart since the texts polish of 2026-10-10, page #90
        // (previously "A Codex that was already running before BrowserAI was installed
        // may need to be restarted to see it."): the page's Codex section says it, without
        // the hedge. And the other ending says what follows.
        await Assert.That(here!).DoesNotContain("restarted");
        await Assert.That(elsewhere!).Contains(Other);
        await Assert.That(elsewhere!).EndsWith("which is not this install, so Codex starts that one.");
        await Assert.That(nowhere!).Contains("No folder on your PATH holds one yet");

        // Claude Code: the same name, and the same three endings in its own words.
        await Assert.That(RegistrationClient.ClaudeCode.ProjectCommandFor(Server, Path.GetDirectoryName(Path.GetDirectoryName(Server))).Command)
            .IsEqualTo(RegistrationTarget.AppFileName);
        await Assert.That(RegistrationClient.ClaudeCode.ProjectCommandFor(Other, @"D:\elsewhere").Command)
            .IsEqualTo(RegistrationTarget.AppFileName);

        var claudeHere = RegistrationClient.ClaudeCode.ProjectNoteAfter(Server, Server);
        var claudeElsewhere = RegistrationClient.ClaudeCode.ProjectNoteAfter(Server, Other);
        var claudeNowhere = RegistrationClient.ClaudeCode.ProjectNoteAfter(Server, null);

        await Assert.That(claudeHere!).StartsWith("The entry names BrowserAI.exe with --mcp and no folder, so the same file is right on every developer's PC; Claude Code finds it on the PATH it was started with.");
        await Assert.That(claudeHere!).EndsWith("It finds this install.");
        await Assert.That(claudeElsewhere!).EndsWith($"The first one on your PATH is '{Other}', which is not this install, so Claude Code starts that one.");
        await Assert.That(claudeNowhere!).Contains("No folder on your PATH holds one yet");
        await Assert.That(claudeHere!).DoesNotContain("Codex");
    }

    /// <summary>One hook pass, the way the hook runs it, against a scratch PATH.</summary>
    private static UserPathReport? Hook(RegistrationIntent intent, string image, ScratchUserPath store, string data) =>
        HookRegistration.Run(
            intent,
            "9.9.9",
            image,
            new FakeRegisterAi(),
            new LocalAppDataPaths(data),
            store,
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId,
            silent: true,
            ask: _ => false,
            clients: [RegistrationClient.ClaudeCode]).PathEntry;
}

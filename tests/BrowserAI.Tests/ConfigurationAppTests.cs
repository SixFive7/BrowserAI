// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using BrowserAI.App;
using BrowserAI.App.Interop;
using BrowserAI.App.Ui;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The configuration app, asserted without a window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything the dialog says is a pure function of an <c>AppState</c></b>,
/// and that is what makes a window application testable at all. What is left
/// over — that Windows draws the page — is covered by
/// <c>RealInstallerTests.TheInstalledMainExecutableOpensOneDialogAndNoConsoleWindow</c>
/// and by <c>TaskDialogLayoutTests</c>; between them the untested remainder is
/// the pixels.
/// </para>
/// <para>
/// ⚠️ <b>Nothing here starts the app.</b> The arms below read the same state
/// object <c>--report</c> serialises and the dialog renders, so a divergence
/// between the two is a red rather than a support artifact that disagrees with
/// the screen.
/// </para>
/// </remarks>
internal sealed class ConfigurationAppTests
{
    /// <summary>
    /// The status sentence names the four states a person can be in, and never
    /// offers to change one it does not own.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStatusSentenceAndTheOfferedActionsFollowTheState()
    {
        using var install = ScratchDirectory.Create("app-state");

        _ = InstalledLayout.Create(install.Path);

        var server = InstalledLayout.ServerIn(install.Path);

        var absent = StateFor(install.Path, server, null, RegistrationOwnership.Absent);

        await Assert.That(absent.StatusSentence()).Contains("Not registered");
        await Assert.That(absent.MayRegister).IsTrue();
        await Assert.That(absent.MayUnregister).IsFalse();

        var ours = StateFor(install.Path, server, server, RegistrationOwnership.OursAndPresent);

        await Assert.That(ours.StatusSentence()).Contains("Registered for all your Claude Code projects");
        await Assert.That(ours.MayRegister).IsFalse();
        await Assert.That(ours.MayUnregister).IsTrue();

        var stale = StateFor(install.Path, server, server + ".gone", RegistrationOwnership.OursAndStale);

        await Assert.That(stale.StatusSentence()).Contains("not there any more");
        await Assert.That(stale.MayRegister).IsTrue();
        await Assert.That(stale.MayUnregister).IsFalse();

        // ⚠️ THE ONE THAT MUST NOT OFFER ANYTHING. A foreign entry is another
        // BrowserAI's, and neither registering over it nor removing it is this
        // product's to do.
        var foreign = StateFor(install.Path, server, @"D:\someone\else\current\BrowserAI.Server.exe", RegistrationOwnership.Foreign);

        await Assert.That(foreign.StatusSentence()).Contains("Another BrowserAI is registered at");
        await Assert.That(foreign.StatusSentence()).Contains(@"D:\someone\else");
        await Assert.That(foreign.MayRegister).IsFalse();
        await Assert.That(foreign.MayUnregister).IsFalse();

        // No client at all: said before anything about registration, because it
        // is the thing a person can act on.
        var clientless = ours with { ClientPath = null };

        await Assert.That(clientless.StatusSentence()).Contains("Claude Code was not found");
        await Assert.That(clientless.MayRegister).IsFalse();
        await Assert.That(clientless.MayUnregister).IsFalse();

        // An unreadable configuration is never read as "not registered", and
        // nothing is offered on top of it.
        var unreadable = ours with
        {
            UserScope = ours.UserScope with { Unreadable = "'x' is not readable JSON, so what is registered there is unknown." },
        };

        await Assert.That(unreadable.StatusSentence()).Contains("unknown");
        await Assert.That(unreadable.MayRegister).IsFalse();
        await Assert.That(unreadable.MayUnregister).IsFalse();
    }

    /// <summary>
    /// The page carries the version, both locations as links, the restart hint
    /// on a first run, and Close as the default button.
    /// </summary>
    /// <remarks>
    /// <b>The restart hint is the sentence that stops a person concluding the
    /// product is broken</b>, and it is asserted verbatim because a paraphrase
    /// of it is a different sentence.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePageSaysWhereEverythingIsAndWhatJustHappened()
    {
        using var install = ScratchDirectory.Create("app-page");

        _ = InstalledLayout.Create(install.Path);

        var state = StateFor(install.Path, InstalledLayout.ServerIn(install.Path), null, RegistrationOwnership.Absent);
        var ordinary = ConfigurationDialog.Page(state, Occasion.Ordinary);

        await Assert.That(ordinary.Title).IsEqualTo("BrowserAI");
        await Assert.That(ordinary.Instruction).Contains(state.Version);
        await Assert.That(ordinary.Content).Contains(install.Path);
        await Assert.That(ordinary.Content).Contains(state.DataRoot);
        await Assert.That(ordinary.Content).Contains(ConfigurationDialog.FolderLinkPrefix);
        await Assert.That(ordinary.Footer!).Contains(ConfigurationDialog.GuideUrl);

        // Not on an ordinary open: it would be a sentence about something that
        // did not just happen.
        await Assert.That(ordinary.Content).DoesNotContain(ConfigurationDialog.RestartHint);

        var first = ConfigurationDialog.Page(state, Occasion.FirstRun);

        await Assert.That(first.Content).Contains("BrowserAI is installed and has registered itself");
        await Assert.That(first.Content).Contains(ConfigurationDialog.RestartHint);

        var updated = ConfigurationDialog.Page(state, Occasion.AfterUpdate);

        await Assert.That(updated.Instruction).StartsWith("Updated to BrowserAI ");

        // A hyperlink asks for a folder or for a URL, and the two are told apart
        // by the prefix rather than by guessing at the string.
        await Assert.That(ConfigurationDialog.FolderFrom(ConfigurationDialog.FolderLinkPrefix + install.Path))
            .IsEqualTo(install.Path);
        await Assert.That(ConfigurationDialog.FolderFrom(ConfigurationDialog.GuideUrl)).IsNull();
    }

    /// <summary>
    /// The update command becomes an apply once a check has found something, and
    /// the apply says what it costs.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdateCommandBecomesAnApplyAndWarnsWhatItCosts()
    {
        using var install = ScratchDirectory.Create("app-update");

        _ = InstalledLayout.Create(install.Path);

        var state = StateFor(install.Path, InstalledLayout.ServerIn(install.Path), null, RegistrationOwnership.Absent);

        var check = ConfigurationDialog.Commands(state, updateAvailable: null)
            .Single(command => command.Id is ConfigurationDialog.Command.CheckForUpdates);

        await Assert.That(check.Text).StartsWith("Check for updates");

        var apply = ConfigurationDialog.Commands(state, updateAvailable: "9.9.9")
            .Single(command => command.Id is ConfigurationDialog.Command.ApplyUpdate);

        await Assert.That(apply.Text).Contains("9.9.9");
        await Assert.That(apply.Text).Contains("lose the server until they are restarted");

        // ⚠️ AND NOT OFFERED AT ALL WHEN THIS IS NOT AN INSTALL. There is
        // nothing to update a `dotnet run` of, and a button that answered
        // "nothing to check" is a button that should not have been there.
        var uninstalled = state with { InstallRoot = null };

        await Assert.That(ConfigurationDialog.Commands(uninstalled, null)
                .Any(command => command.Id is ConfigurationDialog.Command.CheckForUpdates))
            .IsFalse();
    }

    /// <summary>
    /// Every command link has an identifier outside the stock button range.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A collision here would be silent and would fire the wrong
    /// action.</b> <c>IDOK</c> is 1, <c>IDCANCEL</c> 2 and <c>IDCLOSE</c> 8;
    /// those are what the close box and Escape report, and a command sharing one
    /// would be indistinguishable from a person closing the window.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoCommandCanBeMistakenForTheCloseButton()
    {
        using var install = ScratchDirectory.Create("app-ids");

        _ = InstalledLayout.Create(install.Path);

        var state = StateFor(install.Path, InstalledLayout.ServerIn(install.Path), null, RegistrationOwnership.Absent);
        var commands = ConfigurationDialog.Commands(state, "9.9.9");

        await Assert.That(commands).IsNotEmpty();

        foreach (var command in commands)
        {
            await Assert.That(command.Id).IsGreaterThan(100);
            await Assert.That(command.Text).IsNotEmpty();
        }

        await Assert.That(commands.Select(command => command.Id).Distinct().Count()).IsEqualTo(commands.Count);
    }

    /// <summary>
    /// <c>--report</c> writes the state as JSON, and the schema is what the
    /// dialog shows.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheReportCarriesEveryAnswerTheWindowWouldHaveShown()
    {
        using var install = ScratchDirectory.Create("app-report");
        using var output = ScratchDirectory.Create("app-report-out");

        _ = InstalledLayout.Create(install.Path);

        var server = InstalledLayout.ServerIn(install.Path);
        var state = StateFor(install.Path, server, server, RegistrationOwnership.OursAndPresent);
        var path = StatusReport.Write(state, Path.Combine(output.Path, "nested", "report.json"));

        await Assert.That(File.Exists(path)).IsTrue();

        var text = await File.ReadAllTextAsync(path);

        // LF and a trailing newline, like every other file this repository
        // writes.
        await Assert.That(text).DoesNotContain("\r\n");
        await Assert.That(text).EndsWith("\n");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(StatusReport.SchemaVersion);
        await Assert.That(root.GetProperty("product").GetString()).IsEqualTo("BrowserAI");
        await Assert.That(root.GetProperty("version").GetString()).IsEqualTo(state.Version);
        await Assert.That(root.GetProperty("installRoot").GetString()).IsEqualTo(install.Path);
        await Assert.That(root.GetProperty("dataRoot").GetString()).IsEqualTo(state.DataRoot);
        await Assert.That(root.GetProperty("serverCommand").GetString()).IsEqualTo(server);
        await Assert.That(root.GetProperty("clientFound").GetBoolean()).IsTrue();

        // The one sentence the window leads with, in the file that stands in for
        // the window.
        await Assert.That(root.GetProperty("status").GetString()).IsEqualTo(state.StatusSentence());

        var user = root.GetProperty("userScope");

        await Assert.That(user.GetProperty("scope").GetString()).IsEqualTo("User");
        await Assert.That(user.GetProperty("command").GetString()).IsEqualTo(server);
        await Assert.That(user.GetProperty("ownership").GetString()).IsEqualTo("OursAndPresent");
        await Assert.That(user.GetProperty("unreadable").ValueKind).IsEqualTo(JsonValueKind.Null);

        // Absent is a null rather than a missing property: a reader that has to
        // tell "no project file" from "this build did not write the field" has
        // nothing to go on when the field is simply gone.
        await Assert.That(root.GetProperty("projectScope").ValueKind).IsEqualTo(JsonValueKind.Null);

        // And the argument is only honoured when it carries a path.
        await Assert.That(App.Program.ReportPathFrom(["--report", "x"])).IsEqualTo("x");
        await Assert.That(App.Program.ReportPathFrom(["--report"])).IsNull();
        await Assert.That(App.Program.ReportPathFrom([])).IsNull();
    }

    /// <summary>
    /// The nearest project file is found by walking up, and its absence is not a
    /// failure.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNearestProjectFileIsFoundByWalkingUpwards()
    {
        using var scratch = ScratchDirectory.Create("app-project-walk");

        var deep = Directory.CreateDirectory(Path.Combine(scratch.Path, "a", "b", "c"));

        // ⚠️ THE WALK DOES NOT STOP AT THE SCRATCH ROOT, and asserting that it
        // finds NOTHING here would be asserting something about the machine:
        // this scratch tree lives inside a repository that has a `.mcp.json` of
        // its own, so the walk correctly climbs past it and finds that one.
        // What is asserted is the property the walk has — the NEAREST file
        // wins — which is machine-independent, and that nothing inside the
        // scratch tree is claimed before anything is planted there.
        var above = AppState.NearestProject(deep.FullName, scratch.Path);

        if (above is not null)
        {
            await Assert.That(above.File.StartsWith(scratch.Path, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }

        await File.WriteAllTextAsync(
            McpRegistryView.ProjectConfigFile(Path.Combine(scratch.Path, "a")),
            "{ \"mcpServers\": { \"browserai\": { \"command\": \"x\" } } }");

        var found = AppState.NearestProject(deep.FullName, scratch.Path);

        await Assert.That(found).IsNotNull();
        await Assert.That(found!.Scope).IsEqualTo(RegistrationScope.Project);
        await Assert.That(found.Command).IsEqualTo("x");
        await Assert.That(found.File).IsEqualTo(McpRegistryView.ProjectConfigFile(Path.Combine(scratch.Path, "a")));
    }

    /// <summary>
    /// Nothing a click does can throw out of the dialog's callback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>An exception crossing an <c>[UnmanagedCallersOnly]</c> boundary is
    /// a <c>FailFast</c>, not an exception.</b> The runtime cannot unwind into
    /// native frames, so the process is terminated where it stands: no dialog,
    /// no log line, no exit code a person or a script could read — the window
    /// simply vanishes mid-click. Every action this app offers runs inside that
    /// callback, and three of them could reach one today:
    /// <c>Directory.CreateDirectory</c> for the log directory,
    /// <c>Path.Combine</c> outside the reader's own <c>try</c> when
    /// <c>CLAUDE_CONFIG_DIR</c> holds an invalid path, and
    /// <c>Path.GetFullPath</c> on a project directory.
    /// </para>
    /// <para>
    /// <b>Assertable because the dispatch is not the unmanaged method.</b>
    /// <c>Callback</c> resolves the instance and forwards to
    /// <c>Dispatch</c>, which is ordinary managed code — an
    /// <c>[UnmanagedCallersOnly]</c> method cannot be called from C# at all, so
    /// a dispatch written inside one is a dispatch no test can ever reach.
    /// </para>
    /// <para>
    /// <b>No window is ever created here.</b> Every arm runs with
    /// <c>_window</c> at zero, where <c>Rerender</c> and <c>SetContent</c> both
    /// return before they call Windows — so this is the callback's decisions and
    /// nothing else.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NothingAClickDoesCanThrowOutOfTheDialogsCallback()
    {
        var reported = new List<string>();

        using var host = new TaskDialogHost(
            () => throw new InvalidOperationException("the page factory threw"),
            _ => throw new InvalidOperationException("the command threw"),
            _ => throw new InvalidOperationException("the link threw"),
            failure => reported.Add(failure.Message));

        // A hyperlink whose handler throws: reported, and the notification's own
        // answer is still given.
        await Assert.That(host.Dispatch(0, TaskDialogInterop.Notification.HyperlinkClicked, 0, 0))
            .IsEqualTo(TaskDialogInterop.Ok);

        // A command link whose handler throws: reported, and S_FALSE keeps the
        // dialog open rather than closing it on a failure nobody saw.
        await Assert.That(host.Dispatch(0, TaskDialogInterop.Notification.ButtonClicked, ConfigurationDialog.Command.OpenLogs, 0))
            .IsEqualTo(TaskDialogInterop.False);

        await Assert.That(reported).IsEquivalentTo(["the link threw", "the command threw"]);

        // And the third one: a handler that succeeds and asks for a re-render,
        // over a page factory that throws. The factory runs before the window
        // check, so this is the same failure the real one would be.
        reported.Clear();

        using var rerendering = new TaskDialogHost(
            () => throw new InvalidOperationException("the page factory threw"),
            _ => ClickOutcome.Rerender,
            _ => { },
            failure => reported.Add(failure.Message));

        await Assert.That(rerendering.Dispatch(0, TaskDialogInterop.Notification.ButtonClicked, ConfigurationDialog.Command.Register, 0))
            .IsEqualTo(TaskDialogInterop.False);

        // Once, not twice: the report is made, and the attempt to show it must
        // not report its own failure again.
        await Assert.That(reported).IsEquivalentTo(["the page factory threw"]);

        // The control: a host whose delegates do not throw reports nothing, so
        // the arms above fail for their own reason rather than because the
        // reporter fires on every notification.
        reported.Clear();

        using var quiet = new TaskDialogHost(
            () => ConfigurationDialog.Page(StateFor(@"C:\install", @"C:\install\current\BrowserAI.Server.exe", null, RegistrationOwnership.Absent), Occasion.Ordinary, null, null),
            _ => ClickOutcome.Stay,
            _ => { },
            failure => reported.Add(failure.Message));

        await Assert.That(quiet.Dispatch(0, TaskDialogInterop.Notification.HyperlinkClicked, 0, 0))
            .IsEqualTo(TaskDialogInterop.Ok);
        await Assert.That(reported).IsEmpty();
    }

    /// <summary>
    /// A folder that could not be turned into a path is not a cancel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Three outcomes, because two of them used to be one.</b> The picker
    /// answered <see langword="null"/> both when the person closed it and when
    /// <c>SHGetPathFromIDListW</c> refused the chosen item — and the caller read
    /// both as a cancel, so the second closed the picker, wrote nothing and said
    /// nothing at all. The buffer was <c>MAX_PATH</c>, so any folder past 260
    /// characters took that path. <i>Split 2026-09-16.</i>
    /// </para>
    /// <para>
    /// <b>Over constructed inputs, because nothing here can open a modal
    /// window.</b> <c>Decide</c> is the whole of what the picker makes of the two
    /// shell answers, and the P/Invokes around it are the part no run can reach.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFolderThatCouldNotBeTurnedIntoAPathIsNotACancel()
    {
        // Nothing chosen: a cancel, with nothing to say about it.
        await Assert.That(ShellInterop.Decide(chosen: false, resolved: false, null).Outcome)
            .IsEqualTo(FolderPickOutcome.Cancelled);

        // Chosen and resolved: the path, verbatim.
        var picked = ShellInterop.Decide(chosen: true, resolved: true, @"C:\projects	hing");

        await Assert.That(picked.Outcome).IsEqualTo(FolderPickOutcome.Picked);
        await Assert.That(picked.Path).IsEqualTo(@"C:\projects	hing");
        await Assert.That(picked.Reason).IsNull();

        // Chosen and NOT resolved: a failure that carries a sentence, and never
        // a cancel.
        var broke = ShellInterop.Decide(chosen: true, resolved: false, null);

        await Assert.That(broke.Outcome).IsEqualTo(FolderPickOutcome.Failed);
        await Assert.That(broke.Path).IsNull();
        await Assert.That(broke.Reason).IsNotNull();
        await Assert.That(broke.Reason!).Contains("path");

        // Chosen, reported as resolved, and empty: the same, because a folder
        // with no path is not a folder anything can be written into.
        var empty = ShellInterop.Decide(chosen: true, resolved: true, string.Empty);

        await Assert.That(empty.Outcome).IsEqualTo(FolderPickOutcome.Failed);
        await Assert.That(empty.Reason).IsNotNull();
    }

    /// <summary>
    /// The host reports the window a modal child has to be owned by.
    /// </summary>
    /// <remarks>
    /// <b>The value the folder picker is now given.</b> It is zero before
    /// <c>TDN_DIALOG_CREATED</c> and after the dialog closes, and the window in
    /// between — which is what makes
    /// <see cref="HouseRuleTests.EveryFolderPickerIsOwnedByTheDialogThatOpenedIt"/>
    /// a statement about a real value rather than about a spelling.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHostReportsTheWindowAModalChildMustBeOwnedBy()
    {
        using var host = new TaskDialogHost(
            () => ConfigurationDialog.Page(StateFor(@"C:\install", @"C:\install\current\BrowserAI.Server.exe", null, RegistrationOwnership.Absent), Occasion.Ordinary, null, null),
            _ => ClickOutcome.Stay,
            _ => { },
            _ => { });

        await Assert.That(host.Window).IsEqualTo(nint.Zero);

        _ = host.Dispatch(4321, TaskDialogInterop.Notification.Created, 0, 0);

        await Assert.That(host.Window).IsEqualTo((nint)4321);
    }

    /// <summary>
    /// A state with the given registration, and everything else read from this
    /// machine.
    /// </summary>
    private static AppState StateFor(string installRoot, string server, string? registered, RegistrationOwnership ownership) =>
        new()
        {
            Version = "9.9.9",
            InstallRoot = installRoot,
            DataRoot = Path.Combine(installRoot, "data"),
            ServerCommand = server,
            ServerRefusal = null,
            UserScope = new RegistrationView(RegistrationScope.User, "<constructed>", registered, ownership, null),
            ProjectScope = null,
            ClientPath = @"C:\double\claude.exe",
        };
}

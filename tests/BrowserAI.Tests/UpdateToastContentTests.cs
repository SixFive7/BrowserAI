// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;
using System.Xml.Linq;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The four update toasts as Windows is handed them, and the ready toast's live
/// countdown as the data it is updated with, read without raising a toast.
/// </summary>
/// <remarks>
/// <para>
/// <b>T, decided 2026-10-08 by the maintainer, his words verbatim:</b> <i>"t I like
/// the live countdown of the toast. I'd opt for two buttons. Install now and wait
/// for inactivity."</i>, and then <i>"Make sure the toasts have no timeout."</i>
/// So every toast here is a reminder, which Windows keeps on screen until the
/// person acts, and every one carries a button that activates in the background,
/// which the stricter of Microsoft's two pages requires of a reminder.
/// </para>
/// <para>
/// <b>The countdown is the progress element's four bound fields and nothing
/// else</b>, because that is what was measured on 2026-10-08 to update in place,
/// 60 updates of 60 with no new banner and no sound. A bound plain text line was
/// not measured, so no text line here is bound: the reconnect line is written
/// when the toast is raised.
/// </para>
/// <para>
/// <b>Nothing here shows a toast.</b> The suite raises none (Q278), so what is
/// asserted is the XML, the bound values and the arguments each click carries
/// back to the activator.
/// </para>
/// </remarks>
internal sealed partial class UpdateToastContentTests
{
    /// <summary>A fixed moment, on the hour, so the install time reads cleanly.</summary>
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The ready toast says which version waits and that it installs by itself,
    /// names the reconnects the update will cost, binds its countdown and its
    /// holders to the progress element, and offers exactly <i>Install now</i> and
    /// <i>Wait for inactivity</i>, both read back by the activator.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheReadyToastIsAReminderWithInstallNowAndWaitForInactivity()
    {
        var toast = Load(UpdateToastContent.Ready("1.2.0", Holds()));

        await AssertReminder(toast);
        await Assert.That(Joined(Texts(toast))).IsEqualTo(Joined(
        [
            "BrowserAI 1.2.0 is ready to install",
            "It installs by itself once BrowserAI has been idle.",
            "After the update: 1 Claude Code terminal needs /mcp, BrowserAI, Reconnect; 1 Codex conversation needs a new conversation.",
        ]));

        var progress = toast.Descendants("progress").Single();

        await Assert.That((string?)progress.Attribute("title")).IsEqualTo("{progressTitle}");
        await Assert.That((string?)progress.Attribute("value")).IsEqualTo("{progressValue}");
        await Assert.That((string?)progress.Attribute("valueStringOverride")).IsEqualTo("{progressValueString}");
        await Assert.That((string?)progress.Attribute("status")).IsEqualTo("{progressStatus}");

        await Assert.That(Joined(Buttons(toast))).IsEqualTo(Joined(["Install now", "Wait for inactivity"]));
        await Assert.That(Joined(Clicks(toast))).IsEqualTo(Joined(
            [new ToastClick(ToastAction.UpdatePage, "1.2.0"), new ToastClick(ToastAction.Wait, "1.2.0")]));

        // A click on the toast itself does what Install now does.
        await Assert.That(UpdateToastContent.Parse((string?)toast.Root!.Attribute("launch"))).IsEqualTo(new ToastClick(ToastAction.UpdatePage, "1.2.0"));

        // With no relay that will need a reconnect, the toast makes no claim about
        // reconnects at all: a later client could make any such sentence false.
        var quiet = Load(UpdateToastContent.Ready("1.2.0", new UpdateHoldSnapshot(T0, UpdateHoldState.Held, "1.2.0", [], [], [])));

        await Assert.That(Joined(Texts(quiet))).IsEqualTo(Joined(["BrowserAI 1.2.0 is ready to install", "It installs by itself once BrowserAI has been idle."]));
    }

    /// <summary>
    /// Every field the ready toast binds is a value its data supplies, and every value
    /// its data supplies is bound somewhere, and all of them are the progress
    /// element's: a name in one and not the other is a field Windows shows as its
    /// placeholder, or a value nobody sees.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryFieldTheReadyToastBindsIsAProgressFieldItsDataSupplies()
    {
        var xml = UpdateToastContent.Ready("1.2.0", Holds());
        var bound = Placeholder().Matches(xml).Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal).ToList();
        var (values, _) = UpdateToastContent.ReadyData(Holds(), T0, TimeZoneInfo.Utc, default);

        await Assert.That(Joined(bound)).IsEqualTo("progressStatus | progressTitle | progressValue | progressValueString");
        await Assert.That(Joined(values.Keys.Order(StringComparer.Ordinal))).IsEqualTo(Joined(bound));

        // And no text line carries one.
        await Assert.That(Texts(Load(xml)).Any(text => text.Contains('{', StringComparison.Ordinal))).IsFalse();
    }

    /// <summary>
    /// The installing, installed and failed toasts are reminders too, and each says
    /// what it is for and offers what T decided: <i>Dismiss</i>; <i>Changelog</i> and
    /// <i>Dismiss</i>; and <i>Dismiss</i> under the two logs' paths.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstallingInstalledAndFailedToastsSayWhatHappenedAndOfferWhatTDecided()
    {
        var installing = Load(UpdateToastContent.Installing("1.2.0"));

        await AssertReminder(installing);
        await Assert.That(Joined(Texts(installing))).IsEqualTo(Joined(["Installing BrowserAI 1.2.0 now", "BrowserAI starts again by itself when it is done."]));
        await Assert.That(Joined(Buttons(installing))).IsEqualTo(Joined(["Dismiss"]));
        await Assert.That(Joined(Clicks(installing))).IsEqualTo(Joined([new ToastClick(ToastAction.Dismiss, null)]));

        var installed = Load(UpdateToastContent.Installed("1.2.0"));

        await AssertReminder(installed);
        await Assert.That(Joined(Texts(installed))).IsEqualTo(Joined(["BrowserAI 1.2.0 is installed"]));
        await Assert.That(Joined(Buttons(installed))).IsEqualTo(Joined(["Changelog", "Dismiss"]));
        await Assert.That(Joined(Clicks(installed))).IsEqualTo(Joined([new ToastClick(ToastAction.Changelog, "1.2.0"), new ToastClick(ToastAction.Dismiss, null)]));
        await Assert.That(UpdateToastContent.Parse((string?)installed.Root!.Attribute("launch"))).IsEqualTo(new ToastClick(ToastAction.Changelog, "1.2.0"));

        var velopack = @"%LocalAppData%\velopack\velopack_BrowserAI.app.log";
        var own = @"C:\Users\someone\AppData\Local\BrowserAI\logs";
        var failed = Load(UpdateToastContent.Failed("1.2.0", "1.1.0", velopack, own));

        await AssertReminder(failed);
        await Assert.That(Joined(Texts(failed))).IsEqualTo(Joined(
        [
            "The update to 1.2.0 failed",
            "BrowserAI 1.1.0 is still installed.",
            $"What happened is in Velopack's log, {velopack}, and in BrowserAI's log in {own}.",
        ]));
        await Assert.That(Joined(Buttons(failed))).IsEqualTo(Joined(["Dismiss"]));
        await Assert.That(Joined(Clicks(failed))).IsEqualTo(Joined([new ToastClick(ToastAction.Dismiss, null)]));
    }

    /// <summary>
    /// A version or a path is written into the XML as text: markup in one cannot
    /// change the toast, and the click still carries it back exactly.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhatAToastIsGivenIsWrittenAsTextAndReadBackExactly()
    {
        const string Odd = "1.2.0-a&b<c>\"d'e f=g%h";

        var ready = Load(UpdateToastContent.Ready(Odd, Holds()));

        await Assert.That(Texts(ready)[0]).IsEqualTo($"BrowserAI {Odd} is ready to install");
        await Assert.That(Clicks(ready)[0]).IsEqualTo(new ToastClick(ToastAction.UpdatePage, Odd));
        await Assert.That(ready.Descendants("action").Count()).IsEqualTo(2);
        await Assert.That(UpdateToastContent.Parse(UpdateToastContent.Arguments(ToastAction.Changelog, Odd))).IsEqualTo(new ToastClick(ToastAction.Changelog, Odd));

        var failed = Load(UpdateToastContent.Failed(Odd, Odd, "<a>", "&b"));

        await Assert.That(Texts(failed)[2]).IsEqualTo("What happened is in Velopack's log, <a>, and in BrowserAI's log in &b.");
    }

    /// <summary>
    /// The countdown is the longest one still running among everything that holds
    /// the update, counts down by the second, and says when the install would
    /// start; the holders are counted by kind.
    /// </summary>
    /// <remarks>
    /// <b>A relay whose countdown has run out does not hold the update and is not
    /// counted as an agent, and it still needs its reconnect</b>, because every relay
    /// ends when the update installs (H1-T a).
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCountdownIsTheLongestStillRunningAndTheHoldersAreCountedByKind()
    {
        var holds = Holds();

        await Assert.That(holds.WaitAt(T0)).IsEqualTo(new UpdateWaitReading(UpdateWait.Counting, T0.AddMinutes(50)));
        await Assert.That(Joined(holds.HoldingRelaysAt(T0).Select(relay => relay.Client))).IsEqualTo("Claude Code 2.1.290 | Claude Code 2.1.290 (VS Code)");

        var (first, track) = UpdateToastContent.ReadyData(holds, T0, TimeZoneInfo.Utc, default);

        await Assert.That(first["progressTitle"]).IsEqualTo("In use by 2 agents, 1 hidden browser and 1 visible window");
        await Assert.That(first["progressStatus"]).IsEqualTo("Installs in 50:00 if nothing uses it");
        await Assert.That(first["progressValueString"]).IsEqualTo("at 12:50");
        await Assert.That(first["progressValue"]).IsEqualTo("0.0000");
        await Assert.That(track).IsEqualTo(new CountdownTrack(T0.AddMinutes(50), TimeSpan.FromMinutes(50)));

        var (later, _) = UpdateToastContent.ReadyData(holds, T0.AddSeconds(10), TimeZoneInfo.Utc, track);

        await Assert.That(later["progressStatus"]).IsEqualTo("Installs in 49:50 if nothing uses it");
        await Assert.That(later["progressValue"]).IsEqualTo("0.0033");

        // At 8:00 the terminal's relay runs out and stops holding. The hidden
        // browser's countdown ran out at 5:00, and it holds for as long as it is
        // listed, which is while it closes.
        var (eightMinutes, _) = UpdateToastContent.ReadyData(holds, T0.AddMinutes(8), TimeZoneInfo.Utc, track);

        await Assert.That(eightMinutes["progressTitle"]).IsEqualTo("In use by 1 hidden browser and 1 visible window");
        await Assert.That(eightMinutes["progressStatus"]).IsEqualTo("Installs in 42:00 if nothing uses it");
    }

    /// <summary>
    /// When activity moves the longest countdown later, the bar starts again from
    /// empty against the new length, and the install time moves with it.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ActivityThatMovesTheCountdownLaterStartsTheBarAgain()
    {
        var (_, track) = UpdateToastContent.ReadyData(Holds(), T0, TimeZoneInfo.Utc, default);

        // The window had input ten minutes in: an hour from then.
        var moved = Holds() with { VisibleWindows = [new HoldingSession(@"C:\work\window", "A form the person fills in", T0.AddMinutes(70))] };
        var (values, next) = UpdateToastContent.ReadyData(moved, T0.AddMinutes(10), TimeZoneInfo.Utc, track);

        await Assert.That(values["progressValue"]).IsEqualTo("0.0000");
        await Assert.That(values["progressStatus"]).IsEqualTo("Installs in 1:00:00 if nothing uses it");
        await Assert.That(values["progressValueString"]).IsEqualTo("at 13:10");
        await Assert.That(next).IsEqualTo(new CountdownTrack(T0.AddMinutes(70), TimeSpan.FromMinutes(60)));

        // A countdown that moves EARLIER keeps the bar's length: the bar only jumps forward.
        var shorter = Holds() with { VisibleWindows = [new HoldingSession(@"C:\work\window", null, T0.AddMinutes(40))] };
        var (sooner, kept) = UpdateToastContent.ReadyData(shorter, T0.AddMinutes(20), TimeZoneInfo.Utc, next);

        await Assert.That(kept).IsEqualTo(new CountdownTrack(T0.AddMinutes(40), TimeSpan.FromMinutes(60)));
        await Assert.That(sooner["progressValue"]).IsEqualTo("0.6667");

        // And the install time is said in the zone it is given.
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus-two", "plus-two");
        var (zoned, _) = UpdateToastContent.ReadyData(moved, T0.AddMinutes(10), plusTwo, next);

        await Assert.That(zoned["progressValueString"]).IsEqualTo("at 15:10");
    }

    /// <summary>
    /// A holder with no countdown says what it waits for in place of a time: a window
    /// or a session an agent set never to close, a call still running once every
    /// countdown is out, a session still closing, and nothing at all.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHolderWithNoCountdownSaysWhatTheWaitIsFor()
    {
        var empty = new UpdateHoldSnapshot(T0, UpdateHoldState.Held, "1.2.0", [], [], []);

        var window = empty with { VisibleWindows = [new HoldingSession(@"C:\w", null, null)], HiddenSessions = [new HoldingSession(@"C:\h", null, null)] };
        await AssertWait(window, UpdateWait.WindowNeverCloses, "Waits for you to close the window", "no countdown", "0.0000");

        var session = empty with { HiddenSessions = [new HoldingSession(@"C:\h", null, null), new HoldingSession(@"C:\i", null, T0.AddMinutes(3))] };
        await AssertWait(session, UpdateWait.SessionNeverCloses, "Waits for an agent to close its session", "no countdown", "0.0000");

        var call = empty with { Relays = [new HoldingRelay("Codex 0.161.0", @"C:\p", T0.AddMinutes(-2), CallInFlight: true, RelayReconnect.NewConversation)] };
        await AssertWait(call, UpdateWait.CallRunning, "Waits for a running call to finish", "no countdown", "1.0000");
        await Assert.That(UpdateToastContent.ReadyData(call, T0, TimeZoneInfo.Utc, default).Values["progressTitle"]).IsEqualTo("In use by 1 agent");

        var closing = empty with { HiddenSessions = [new HoldingSession(@"C:\h", null, T0.AddSeconds(-5))] };
        await AssertWait(closing, UpdateWait.Closing, "Installs once the last browser has closed", "soon", "1.0000");

        var idle = empty with { Relays = [new HoldingRelay("Claude Code 2.1.290", null, T0.AddMinutes(-1), CallInFlight: false, RelayReconnect.None)] };
        await AssertWait(idle, UpdateWait.NothingHolds, "Installs in a moment", "now", "1.0000");
        await Assert.That(UpdateToastContent.ReadyData(idle, T0, TimeZoneInfo.Utc, default).Values["progressTitle"]).IsEqualTo("Nothing uses BrowserAI now");
        await Assert.That(UpdateToastContent.Reconnects(idle)).IsNull();
    }

    /// <summary>
    /// Several of a kind are counted in the plural, every relay's reconnect is
    /// counted whether it holds the update or not, and a client whose reconnect
    /// nobody could tell is counted as one that may need one.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task SeveralOfAKindAreCountedAndAnUnknownClientIsNamed()
    {
        var holds = new UpdateHoldSnapshot(
            T0,
            UpdateHoldState.Held,
            "1.2.0",
            [new HoldingSession(@"C:\a", null, T0.AddMinutes(1)), new HoldingSession(@"C:\b", null, T0.AddMinutes(2))],
            [new HoldingSession(@"C:\c", null, T0.AddMinutes(3)), new HoldingSession(@"C:\d", null, T0.AddMinutes(4))],
            [
                new HoldingRelay("Claude Code", null, T0.AddMinutes(5), CallInFlight: false, RelayReconnect.McpReconnect),
                new HoldingRelay("Claude Code", null, T0.AddMinutes(-5), CallInFlight: false, RelayReconnect.McpReconnect),
                new HoldingRelay("Codex", null, T0.AddMinutes(5), CallInFlight: false, RelayReconnect.NewConversation),
                new HoldingRelay("Codex", null, T0.AddMinutes(5), CallInFlight: false, RelayReconnect.NewConversation),
                new HoldingRelay("someclient", null, T0.AddMinutes(5), CallInFlight: false, RelayReconnect.Unknown),
                new HoldingRelay("Claude Code (VS Code)", null, T0.AddMinutes(5), CallInFlight: false, RelayReconnect.None),
            ]);

        var (values, _) = UpdateToastContent.ReadyData(holds, T0, TimeZoneInfo.Utc, default);

        await Assert.That(values["progressTitle"]).IsEqualTo("In use by 5 agents, 2 hidden browsers and 2 visible windows");
        await Assert.That(UpdateToastContent.Reconnects(holds)).IsEqualTo(
            "After the update: 2 Claude Code terminals need /mcp, BrowserAI, Reconnect; 2 Codex conversations need a new conversation; 1 client may need BrowserAI reconnected.");
    }

    /// <summary>
    /// Each reconnect names its conversation as its VS Code tab shows it: a title cut to
    /// 24 characters and three full stops, in quotes, and BrowserAI's own words as they
    /// are; two names of a kind are spelled out and the rest counted; and a client named
    /// nothing is counted as before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.2 a, the maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept
    /// all your recommendations"</i></b>: the person is told which conversations will
    /// need a reconnect, by the name each one has on the screen. A VS Code tab shows at
    /// most 25 characters, read in the extension's webview at 2.1.292.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a line that counted every relay as before,
    /// and against one that spelled out every name.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachReconnectNamesItsConversationAsItsTabShowsIt()
    {
        static HoldingRelay relay(RelayReconnect reconnect, ConversationName? name) =>
            new("Claude Code 2.1.296", @"C:\Source\BrowserAI", T0.AddMinutes(5), CallInFlight: false, reconnect, Label: name);

        var holds = new UpdateHoldSnapshot(T0, UpdateHoldState.Held, "1.2.0", [], [],
        [
            relay(RelayReconnect.McpReconnect, new ConversationName("Fix the login bug", IsTitle: true)),
            relay(RelayReconnect.McpReconnect, new ConversationName("Refactor the session index for speed", IsTitle: true)),
            relay(RelayReconnect.McpReconnect, new ConversationName("Claude Code in BrowserAI", IsTitle: false)),
            relay(RelayReconnect.McpReconnect, null),
            relay(RelayReconnect.NewConversation, new ConversationName("Codex in BrowserAI", IsTitle: false)),
            relay(RelayReconnect.Unknown, null),
            relay(RelayReconnect.None, new ConversationName("A tab that comes back by itself", IsTitle: true)),
        ]);

        await Assert.That(UpdateToastContent.Reconnects(holds)).IsEqualTo(
            "After the update: \"Fix the login bug\", \"Refactor the session ind...\" and 2 more need /mcp, BrowserAI, Reconnect;"
            + " Codex in BrowserAI needs a new conversation; 1 client may need BrowserAI reconnected.");

        // One of a kind, named: its name and the singular.
        var one = holds with { Relays = [relay(RelayReconnect.McpReconnect, new ConversationName("Fix the login bug", IsTitle: true))] };

        await Assert.That(UpdateToastContent.Reconnects(one)).IsEqualTo("After the update: \"Fix the login bug\" needs /mcp, BrowserAI, Reconnect.");

        // The ready toast carries the line as its third.
        await Assert.That(Texts(Load(UpdateToastContent.Ready("1.2.0", one)))[2]).IsEqualTo("After the update: \"Fix the login bug\" needs /mcp, BrowserAI, Reconnect.");

        // A cut never leaves half a surrogate pair.
        await Assert.That(ConversationName.Cut(new string('a', 23) + "\U0001F600" + "bbb")).IsEqualTo(new string('a', 23) + "...");
        await Assert.That(ConversationName.Cut("Exactly twenty-five chars")).IsEqualTo("Exactly twenty-five chars");
    }

    /// <summary>What is left is whole seconds rounded up, in minutes and seconds, and in hours from an hour on.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhatIsLeftIsWholeSecondsRoundedUp()
    {
        await Assert.That(UpdateToastContent.Remaining(TimeSpan.Zero)).IsEqualTo("0:00");
        await Assert.That(UpdateToastContent.Remaining(TimeSpan.FromSeconds(-3))).IsEqualTo("0:00");
        await Assert.That(UpdateToastContent.Remaining(TimeSpan.FromMilliseconds(9_400))).IsEqualTo("0:10");
        await Assert.That(UpdateToastContent.Remaining(TimeSpan.FromMilliseconds(570_400))).IsEqualTo("9:31");
        await Assert.That(UpdateToastContent.Remaining(TimeSpan.FromMinutes(50))).IsEqualTo("50:00");
        await Assert.That(UpdateToastContent.Remaining(TimeSpan.FromHours(1))).IsEqualTo("1:00:00");
        await Assert.That(UpdateToastContent.Remaining(new TimeSpan(1, 2, 3))).IsEqualTo("1:02:03");
    }

    /// <summary>A click is read back from its arguments, and anything else is nothing.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClickIsReadBackFromItsArgumentsAndAnythingElseIsNothing()
    {
        await Assert.That(UpdateToastContent.Parse("action=update-page&version=1.2.0")).IsEqualTo(new ToastClick(ToastAction.UpdatePage, "1.2.0"));
        await Assert.That(UpdateToastContent.Parse("action=install-now&version=1.2.0")).IsEqualTo(new ToastClick(ToastAction.UpdatePage, "1.2.0"));
        await Assert.That(UpdateToastContent.Parse("action=wait&version=1.2.0")).IsEqualTo(new ToastClick(ToastAction.Wait, "1.2.0"));
        await Assert.That(UpdateToastContent.Parse("action=changelog&version=1.2.0")).IsEqualTo(new ToastClick(ToastAction.Changelog, "1.2.0"));
        await Assert.That(UpdateToastContent.Parse("action=dismiss")).IsEqualTo(new ToastClick(ToastAction.Dismiss, null));
        await Assert.That(UpdateToastContent.Parse("version=1.2.0&action=wait")).IsEqualTo(new ToastClick(ToastAction.Wait, "1.2.0"));

        await Assert.That(UpdateToastContent.Parse("action=review")).IsEqualTo(new ToastClick(ToastAction.None, null));
        await Assert.That(UpdateToastContent.Parse("action=review&version=1.2.0")).IsEqualTo(new ToastClick(ToastAction.None, null));
        await Assert.That(UpdateToastContent.Parse("nonsense")).IsEqualTo(new ToastClick(ToastAction.None, null));
        await Assert.That(UpdateToastContent.Parse(string.Empty)).IsEqualTo(new ToastClick(ToastAction.None, null));
        await Assert.That(UpdateToastContent.Parse(null)).IsEqualTo(new ToastClick(ToastAction.None, null));
    }

    /// <summary>What every update toast carries: a reminder, and only buttons the activator reads back.</summary>
    /// <param name="toast">The toast.</param>
    /// <returns>The assertion task.</returns>
    private static async Task AssertReminder(XDocument toast)
    {
        await Assert.That(toast.Root!.Name.LocalName).IsEqualTo("toast");
        await Assert.That((string?)toast.Root.Attribute("scenario")).IsEqualTo("reminder");
        await Assert.That((string?)toast.Descendants("binding").Single().Attribute("template")).IsEqualTo("ToastGeneric");

        var actions = toast.Descendants("action").ToList();

        await Assert.That(actions).IsNotEmpty();

        foreach (var action in actions)
        {
            await Assert.That((string?)action.Attribute("activationType")).IsEqualTo("background");
            await Assert.That(UpdateToastContent.Parse((string?)action.Attribute("arguments")).Action).IsNotEqualTo(ToastAction.None);
        }

        await Assert.That(UpdateToastContent.Parse((string?)toast.Root.Attribute("launch")).Action).IsNotEqualTo(ToastAction.None);
    }

    /// <summary>One holder-less wait's three bound words.</summary>
    /// <param name="holds">What holds the update.</param>
    /// <param name="wait">The wait it must read as.</param>
    /// <param name="status">The status it must say.</param>
    /// <param name="valueString">The words beside the bar.</param>
    /// <param name="value">The bar.</param>
    /// <returns>The assertion task.</returns>
    private static async Task AssertWait(UpdateHoldSnapshot holds, UpdateWait wait, string status, string valueString, string value)
    {
        await Assert.That(holds.WaitAt(T0)).IsEqualTo(new UpdateWaitReading(wait, null));

        var (values, _) = UpdateToastContent.ReadyData(holds, T0, TimeZoneInfo.Utc, default);

        await Assert.That(values["progressStatus"]).IsEqualTo(status);
        await Assert.That(values["progressValueString"]).IsEqualTo(valueString);
        await Assert.That(values["progressValue"]).IsEqualTo(value);
    }

    /// <summary>
    /// A held update: a hidden browser closing in five minutes, a visible window in
    /// fifty, a terminal relay in eight, a VS Code relay in three, and a Codex relay
    /// already out.
    /// </summary>
    /// <returns>The snapshot.</returns>
    private static UpdateHoldSnapshot Holds() => new(
        T0,
        UpdateHoldState.Held,
        "1.2.0",
        [new HoldingSession(@"C:\work\hidden", "Reads the release notes", T0.AddMinutes(5))],
        [new HoldingSession(@"C:\work\window", "A form the person fills in", T0.AddMinutes(50))],
        [
            new HoldingRelay("Claude Code 2.1.290", @"C:\Source\one", T0.AddMinutes(8), CallInFlight: false, RelayReconnect.McpReconnect),
            new HoldingRelay("Codex 0.161.0", @"C:\Source\two", T0.AddMinutes(-1), CallInFlight: false, RelayReconnect.NewConversation),
            new HoldingRelay("Claude Code 2.1.290 (VS Code)", @"C:\Source\three", T0.AddMinutes(3), CallInFlight: false, RelayReconnect.None),
        ]);

    private static XDocument Load(string xml) => XDocument.Parse(xml);

    /// <summary>A list as one line, so an ordered comparison fails with both sides readable.</summary>
    /// <typeparam name="T">The item.</typeparam>
    /// <param name="items">The list.</param>
    /// <returns>The items, in order.</returns>
    private static string Joined<T>(IEnumerable<T> items) => string.Join(" | ", items);

    private static List<string> Texts(XDocument toast) =>
        [.. toast.Descendants("binding").Single().Elements("text").Select(text => text.Value)];

    private static List<string> Buttons(XDocument toast) =>
        [.. toast.Descendants("action").Select(action => (string?)action.Attribute("content") ?? string.Empty)];

    private static List<ToastClick> Clicks(XDocument toast) =>
        [.. toast.Descendants("action").Select(action => UpdateToastContent.Parse((string?)action.Attribute("arguments")))];

    /// <summary>A bound field in toast XML: <c>{name}</c>.</summary>
    [GeneratedRegex(@"\{([A-Za-z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}

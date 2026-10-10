// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;
using System.Xml.Linq;
using BrowserAI.Tests.Harness;
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
        var toast = Load(UpdateToastContent.Ready("1.2.0", Holds(), "1.1.0"));

        await AssertReminder(toast);
        await Assert.That(Joined(Texts(toast))).IsEqualTo(Joined(
        [
            "BrowserAI 1.2.0 is ready to install",
            "It installs by itself once BrowserAI has been idle.",
            "After the update: run /mcp, BrowserAI, Reconnect in 1 Claude Code terminal; 1 Codex conversation needs a new one.",
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
        var quiet = Load(UpdateToastContent.Ready("1.2.0", new UpdateHoldSnapshot(T0, UpdateHoldState.Held, "1.2.0", [], [], []), "1.1.0"));

        await Assert.That(Joined(Texts(quiet))).IsEqualTo(Joined(["BrowserAI 1.2.0 is ready to install", "It installs by itself once BrowserAI has been idle."]));
    }

    /// <summary>
    /// A version older than the one installed says so in the ready toast's title, and
    /// a newer one's title says nothing of the kind.
    /// </summary>
    /// <remarks>
    /// <b>Q308 a, the maintainer's words of 2026-10-03 verbatim: <i>"Q308 a"</i></b>:
    /// automatic rollback stays, and the interface says when the offered version is
    /// older. Built again 2026-10-10, after the page's own check, which said it, was
    /// deleted under his "9 a".
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnOlderVersionsReadyToastSaysItIsOlderThanTheInstalledOne()
    {
        var older = Texts(Load(UpdateToastContent.Ready("1.1.0", Holds() with { Version = "1.1.0", Older = true }, "1.2.0")));

        // ⚠️ On the second line since the on-screen check of 2026-10-10: in the title the
        // banner ended a line inside the installed version, at its hyphen (previously
        // "BrowserAI 1.1.0, older than 1.2.0, is ready to install" and the usual second line).
        await Assert.That(older[0]).IsEqualTo("BrowserAI 1.1.0 is ready to install");
        await Assert.That(older[1]).IsEqualTo("It is older than 1.2.0.");

        var newer = Texts(Load(UpdateToastContent.Ready("1.2.0", Holds(), "1.1.0")));

        await Assert.That(newer[0]).IsEqualTo("BrowserAI 1.2.0 is ready to install");
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
        var xml = UpdateToastContent.Ready("1.2.0", Holds(), "1.1.0");
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
            $@"Velopack's log is in %LocalAppData%\velopack, and BrowserAI's in {own}.",
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

        var ready = Load(UpdateToastContent.Ready(Odd, Holds(), "1.1.0"));

        await Assert.That(Texts(ready)[0]).IsEqualTo($"BrowserAI {Odd} is ready to install");
        await Assert.That(Clicks(ready)[0]).IsEqualTo(new ToastClick(ToastAction.UpdatePage, Odd));
        await Assert.That(ready.Descendants("action").Count()).IsEqualTo(2);
        await Assert.That(UpdateToastContent.Parse(UpdateToastContent.Arguments(ToastAction.Changelog, Odd))).IsEqualTo(new ToastClick(ToastAction.Changelog, Odd));

        var failed = Load(UpdateToastContent.Failed(Odd, Odd, "<a>", "&b"));

        await Assert.That(Texts(failed)[2]).IsEqualTo("Velopack's log is in <a>, and BrowserAI's in &b.");
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

        await Assert.That(first["progressTitle"]).IsEqualTo("2 agents, 1 hidden browser, 1 window");
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

        await Assert.That(eightMinutes["progressTitle"]).IsEqualTo("1 hidden browser, 1 window");
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
        await Assert.That(UpdateToastContent.ReadyData(call, T0, TimeZoneInfo.Utc, default).Values["progressTitle"]).IsEqualTo("1 agent");

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

        await Assert.That(values["progressTitle"]).IsEqualTo("5 agents, 2 hidden browsers, 2 windows");
        await Assert.That(UpdateToastContent.Reconnects(holds)).IsEqualTo(
            "After the update: run /mcp, BrowserAI, Reconnect in 2 Claude Code terminals; 2 Codex conversations need a new one; 1 client may need a reconnect.");
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
    /// <para>
    /// <b>Its first line names one kind alone</b> since #47 of the texts review the same
    /// day: with three kinds in it the line no longer fits the banner, and it is counted,
    /// which <see cref="EveryToastsTextShowsWholeInItsBanner"/> holds.
    /// <i>Changed 2026-10-10 (previously the first line named all three kinds, as
    /// <i>After the update: "Fix the login bug", "Refactor the session ind..." and 2 more
    /// need /mcp, BrowserAI, Reconnect; Codex in BrowserAI needs a new conversation; 1
    /// client may need BrowserAI reconnected.</i>).</i>
    /// </para>
    /// <para>
    /// ⚠️ <b>The command first, and no title split, since later on 2026-10-10</b>, after
    /// that day's on-screen check: the line reads <i>After the update: run /mcp, BrowserAI,
    /// Reconnect in</i> and then who needs it, and a form that would put a line end inside
    /// a title is passed over for one that names fewer. <i>Changed that day (previously
    /// <i>After the update: "Fix the login bug", "Refactor the session ind..." and 2 more
    /// need /mcp, BrowserAI, Reconnect.</i>).</i>
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

        var terminals = holds with { Relays = [.. holds.Relays.Where(relay => relay.Reconnect is RelayReconnect.McpReconnect)] };

        // ⚠️ Since the on-screen check of 2026-10-10, a form a line would end inside a name of
        // is passed over, and at 50 characters a line the second title here would be split
        // after "the session", so one name is spelled out and the rest counted.
        await Assert.That(UpdateToastContent.Reconnects(terminals)).IsEqualTo(
            "After the update: run /mcp, BrowserAI, Reconnect in \"Fix the login bug\" and 3 more.");

        // Two titles that fit whole are both spelled out.
        var two = holds with
        {
            Relays =
            [
                relay(RelayReconnect.McpReconnect, new ConversationName("Fix the login bug", IsTitle: true)),
                relay(RelayReconnect.McpReconnect, new ConversationName("Write the docs", IsTitle: true)),
            ],
        };

        await Assert.That(UpdateToastContent.Reconnects(two)).IsEqualTo(
            "After the update: run /mcp, BrowserAI, Reconnect in \"Fix the login bug\" and \"Write the docs\".");

        // A Codex conversation named needs a new conversation; counted, a new one.
        var codex = holds with { Relays = [.. holds.Relays.Where(relay => relay.Reconnect is RelayReconnect.NewConversation)] };

        await Assert.That(UpdateToastContent.Reconnects(codex)).IsEqualTo("After the update: Codex in BrowserAI needs a new conversation.");

        // All three kinds at once are too many names for the banner, so they are counted.
        await Assert.That(UpdateToastContent.Reconnects(holds)).IsEqualTo(
            "After the update: run /mcp, BrowserAI, Reconnect in 4 Claude Code terminals; 1 Codex conversation needs a new one; 1 client may need a reconnect.");

        // One of a kind, named: its name and the singular.
        var one = holds with { Relays = [relay(RelayReconnect.McpReconnect, new ConversationName("Fix the login bug", IsTitle: true))] };

        await Assert.That(UpdateToastContent.Reconnects(one)).IsEqualTo("After the update: run /mcp, BrowserAI, Reconnect in \"Fix the login bug\".");

        // The ready toast carries the line as its third.
        await Assert.That(Texts(Load(UpdateToastContent.Ready("1.2.0", one, "1.1.0")))[2]).IsEqualTo("After the update: run /mcp, BrowserAI, Reconnect in \"Fix the login bug\".");

        // A cut never leaves half a surrogate pair.
        await Assert.That(ConversationName.Cut(new string('a', 23) + "\U0001F600" + "bbb")).IsEqualTo(new string('a', 23) + "...");
        await Assert.That(ConversationName.Cut("Exactly twenty-five chars")).IsEqualTo("Exactly twenty-five chars");
    }

    /// <summary>
    /// The holders line fits the banner at the longest counts a machine realistically
    /// reaches: up to two digits of each kind, with all three kinds at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>2.3 a, the maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept
    /// all your recommendations"</i></b>, which took the direction to shorten the
    /// wording, <i>"for example '3 agents, 2 hidden browsers, 1 window'"</i>. Measured on his screen on 2026-10-08: the line <i>In use by 3
    /// agents, 2 hidden browsers and 1 visible window</i> showed in the 362 px banner
    /// as far as <i>1 visible wi</i>.
    /// </para>
    /// <para>
    /// <b>Every combination of 0, 1, 9 and 99 of each kind is asked for</b>, so the
    /// singular, the plural, one digit and two are each met beside every other. The
    /// measured line is the positive control: it is over the budget, so a budget
    /// that let anything through would fail here first.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHoldersLineFitsTheBannerAtTheLongestRealisticCounts()
    {
        const string Measured = "In use by 3 agents, 2 hidden browsers and 1 visible window";

        await Assert.That(Measured.Length).IsGreaterThan(UpdateToastContent.HoldersLineCharacters);

        int[] counts = [0, 1, 9, 99];
        var lines = new List<string>();

        foreach (var agents in counts)
        {
            foreach (var hidden in counts)
            {
                foreach (var visible in counts)
                {
                    var (values, _) = UpdateToastContent.ReadyData(Counted(agents, hidden, visible), T0, TimeZoneInfo.Utc, default);

                    lines.Add(values[UpdateToastContent.HoldersField]);
                }
            }
        }

        var longest = lines.MaxBy(line => line.Length)!;

        await Assert.That(lines.Count).IsEqualTo(64);
        await Assert.That(longest.Length)
            .IsLessThanOrEqualTo(UpdateToastContent.HoldersLineCharacters)
            .Because($"'{longest}' is {longest.Length} characters, and the banner showed {UpdateToastContent.HoldersLineCharacters} of the measured line whole");
    }

    /// <summary>
    /// Every toast's text shows whole in its banner, at the longest counts and the names a
    /// machine realistically reaches and with a default install's paths: a title in the two
    /// lines a title has, and the second and third lines in the four the two share.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>#47 and #60 of the texts review of 2026-10-10</b>, held to the banner the holders
    /// line was measured in. The crops of 2026-10-08 show the banner wrapping a text line at
    /// the width the holders line's 54 characters take, and a count of characters is no
    /// stand-in for that width here: a line of 53 characters of the failed toast did not
    /// fit. So this holds the width itself, through <see cref="BannerText"/>, which is
    /// measured against those crops first.
    /// </para>
    /// <para>
    /// <b>Every combination is asked for</b>: none, 1, 2, 9 or 99 of each kind of
    /// reconnect, counted or carrying names, a title a tab cuts or BrowserAI's own words
    /// with a folder in them; the installing, installed and failed toasts of a
    /// seven-character version with the default install's two log folders; and the broken
    /// install's toast. <i>Added 2026-10-10</i>: an older version's ready toast (Q308 a),
    /// whose title names both versions, beside the longest reconnect line.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against the wording before it, which counted five
    /// lines of description for a reconnect line of three kinds, and five for the failed
    /// toast with Velopack's whole path in it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryToastsTextShowsWholeInItsBanner()
    {
        // The instrument first, against what the banner showed on 2026-10-08.
        foreach (var line in BannerText.ShownWhole)
        {
            await Assert.That(BannerText.Width(line)).IsLessThanOrEqualTo(BannerText.LinePixels).Because($"the banner showed '{line}' whole");
        }

        foreach (var text in BannerText.DidNotFit)
        {
            await Assert.That(BannerText.Width(text)).IsGreaterThan(BannerText.LinePixels).Because($"the banner did not fit '{text}' on one line");
        }

        foreach (var (text, lines) in BannerText.Wrapped)
        {
            await Assert.That(BannerText.Lines(text).Count).IsGreaterThanOrEqualTo(lines).Because($"the banner took {lines} lines for '{text}'");
        }

        // The reconnect line has what the second line leaves of the four, and an older
        // version's second line leaves the same, at a version of 17 characters and one of 22.
        await Assert.That(BannerText.DescriptionLines - BannerText.Lines(UpdateToastContent.InstallsWhenIdle).Count).IsEqualTo(UpdateToastContent.ReconnectLines);

        foreach (var installed in new[] { "1.1.1-alpha.0.325", "10.10.10-alpha.10.1001" })
        {
            await Assert.That(BannerText.DescriptionLines - BannerText.Lines(UpdateToastContent.OlderThan(installed)).Count)
                .IsEqualTo(UpdateToastContent.ReconnectLines)
                .Because($"an older version's second line beside {installed} takes one line of the four");
        }

        var toasts = EveryToast("1.10.10", "1.10.9", "1.10.11");
        var over = new List<string>();
        var named = 0;

        foreach (var (what, xml) in toasts)
        {
            var texts = Texts(Load(xml));
            var title = BannerText.Lines(texts[0]).Count;
            var description = texts.Skip(1).Sum(text => BannerText.Lines(text).Count);

            if (what.StartsWith("ready", StringComparison.Ordinal) && texts.Count > 2
                && (texts[2].Contains('"', StringComparison.Ordinal) || texts[2].Contains(" in ", StringComparison.Ordinal)))
            {
                named++;
            }

            if (title > BannerText.TitleLines || description > BannerText.DescriptionLines)
            {
                over.Add($"{what}: the title takes {title} lines and the rest {description}: {string.Join(" / ", texts.Skip(1))}");
            }
        }

        await Assert.That(toasts.Count).IsEqualTo((States.Length * States.Length * States.Length) + 5);
        await Assert.That(named).IsGreaterThan(0).Because("a line with room for names spells them out");
        await Assert.That(string.Join(Environment.NewLine, over)).IsEmpty();
    }

    /// <summary>
    /// No toast's text puts a line end inside a command, a version or a quoted name: each
    /// shows on one line of the banner, at every count and with names of every kind, and
    /// with versions that carry a pre-release label.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The on-screen check of 2026-10-10, at the maintainer's 28 b</b>: the banner ended a
    /// line inside <i>/mcp</i> in the counted reconnect line
    /// (<i>need /</i>, then <i>mcp, BrowserAI, Reconnect</i>), inside the version
    /// <i>1.1.1-alpha.0.325</i> at its hyphen in an older version's title, and inside the
    /// title <i>"Summarise the open issues"</i> in the reconnect line with names. The
    /// banner breaks after a slash or a hyphen that a letter follows, as well as at a space,
    /// and <see cref="BannerText"/> breaks there too since that day; its wrapping is held
    /// first to five of that day's texts line for line, and to the three splits.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against the wording of that morning, where it named
    /// each of the three: <i>/mcp</i> split in the counted line of 99 terminals, the
    /// installed version split in the older title, and a title split in the line that names
    /// the clients that may need a reconnect.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoCommandVersionOrQuotedNameIsSplitAcrossTwoLines()
    {
        // The instrument first: the lines the banner gave five texts of 2026-10-10.
        foreach (var (text, lines) in BannerText.WrappedExactly)
        {
            await Assert.That(Joined(BannerText.Lines(text))).IsEqualTo(Joined(lines)).Because($"the banner gave '{text}' these lines on 2026-10-10");
        }

        // And the three splits it showed that day, which the check below must be able to see.
        await Assert.That(Joined(BannerText.SplitInside(BannerText.WrappedExactly[0].Text, ["/mcp"]))).IsEqualTo("/mcp");
        await Assert.That(Joined(BannerText.SplitInside(BannerText.WrappedExactly[1].Text, ["\"Summarise the open issues\""]))).IsEqualTo("\"Summarise the open issues\"");
        await Assert.That(Joined(BannerText.SplitInside(BannerText.WrappedExactly[2].Text, ["1.1.1-alpha.0.323", "1.1.1-alpha.0.325"]))).IsEqualTo("1.1.1-alpha.0.325");

        var split = new List<string>();
        var looked = 0;

        foreach (var (version, earlier, later) in new[] { ("1.1.1-alpha.0.323", "1.1.1-alpha.0.322", "1.1.1-alpha.0.325"), ("10.10.10-alpha.10.1000", "10.10.10-alpha.10.999", "10.10.10-alpha.10.1001") })
        {
            foreach (var (what, xml) in EveryToast(version, earlier, later))
            {
                foreach (var text in Texts(Load(xml)))
                {
                    string[] pieces = [.. Commands, version, earlier, later, .. QuotedName().Matches(text).Select(match => match.Value)];

                    looked++;
                    split.AddRange(BannerText.SplitInside(text, pieces).Select(piece => $"{what}, {version}: {piece} is split in: {text}"));
                }
            }
        }

        await Assert.That(looked).IsGreaterThan(2 * States.Length * States.Length * States.Length);
        await Assert.That(string.Join(Environment.NewLine, split)).IsEmpty();
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

    /// <summary>
    /// A held update with so many agents holding it, hidden browsers and visible
    /// windows, every one of them counting down from ten minutes on.
    /// </summary>
    /// <param name="agents">How many relays hold it.</param>
    /// <param name="hidden">How many hidden browsers.</param>
    /// <param name="visible">How many visible windows.</param>
    /// <returns>The snapshot.</returns>
    private static UpdateHoldSnapshot Counted(int agents, int hidden, int visible) => new(
        T0,
        UpdateHoldState.Held,
        "1.2.0",
        [.. Enumerable.Range(0, hidden).Select(index => new HoldingSession($@"C:\work\hidden-{index}", null, T0.AddMinutes(10)))],
        [.. Enumerable.Range(0, visible).Select(index => new HoldingSession($@"C:\work\window-{index}", null, T0.AddMinutes(10)))],
        [.. Enumerable.Range(0, agents).Select(index => new HoldingRelay("Claude Code", null, T0.AddMinutes(10), CallInFlight: false, RelayReconnect.None))]);

    /// <summary>How many of one kind of reconnect a toast is composed with, and whether they carry names: none, 1, 2, 9 or 99, counted or named.</summary>
    private static readonly (int Count, bool Named)[] States = [(0, false), (1, false), (2, false), (9, false), (99, false), (1, true), (2, true), (9, true), (99, true)];

    /// <summary>The three kinds of reconnect, in the order the line names them.</summary>
    private static readonly RelayReconnect[] Kinds = [RelayReconnect.McpReconnect, RelayReconnect.NewConversation, RelayReconnect.Unknown];

    /// <summary>Each kind's names: titles a tab cuts and BrowserAI's own words with a folder in them.</summary>
    private static readonly ConversationName[][] Names =
    [
        [new("Refactor the session index for speed", IsTitle: true), new("Investigate the flaky test in CI", IsTitle: true), new("Claude Code in SixFive7-BrowserAI", IsTitle: false)],
        [new("Codex in SixFive7-BrowserAI", IsTitle: false), new("Review The Release Notes For 1.2.0", IsTitle: true), new("Write the migration guide", IsTitle: true)],
        [new("unnamed conversation in RegisterAI", IsTitle: false), new("Summarise the open issues", IsTitle: true), new("Plan the next release", IsTitle: true)],
    ];

    /// <summary>The commands a toast tells a person to type, which must show on one line.</summary>
    private static readonly string[] Commands = ["/mcp"];

    /// <summary>
    /// Every toast BrowserAI raises, at every combination of <see cref="States"/> for the
    /// three kinds, an older version's beside the longest reconnect line, and the
    /// installing, installed, failed and broken install's toasts.
    /// </summary>
    /// <param name="version">The version a toast is about.</param>
    /// <param name="earlier">The version installed before it, which a failed toast names as still installed.</param>
    /// <param name="later">A version newer than it, which an older version's ready toast is older than.</param>
    /// <returns>Each toast's description and XML.</returns>
    private static List<(string What, string Xml)> EveryToast(string version, string earlier, string later)
    {
        var toasts = new List<(string What, string Xml)>();

        foreach (var terminals in States)
        {
            foreach (var codex in States)
            {
                foreach (var others in States)
                {
                    List<HoldingRelay> relays =
                    [
                        .. RelaysOf(Kinds[0], terminals, Names[0]),
                        .. RelaysOf(Kinds[1], codex, Names[1]),
                        .. RelaysOf(Kinds[2], others, Names[2]),
                    ];

                    var holds = new UpdateHoldSnapshot(T0, UpdateHoldState.Held, version, [], [], relays);

                    toasts.Add(($"ready, {terminals}, {codex}, {others}", UpdateToastContent.Ready(version, holds, earlier)));
                }
            }
        }

        var velopack = UpdateToasts.VelopackLogFor("velopack.BrowserAI.app");
        var own = UpdateToasts.Shortened(@"C:\Users\someone\AppData\Local\BrowserAI\logs", @"C:\Users\someone\AppData\Local");

        // Q308 a: an older version's title, with the longest reconnect line beside it.
        List<HoldingRelay> crowded =
        [
            .. RelaysOf(Kinds[0], States[^1], Names[0]),
            .. RelaysOf(Kinds[1], States[^1], Names[1]),
            .. RelaysOf(Kinds[2], States[^1], Names[2]),
        ];

        toasts.Add(("ready, older", UpdateToastContent.Ready(version, new UpdateHoldSnapshot(T0, UpdateHoldState.Held, version, [], [], crowded, Older: true), later)));
        toasts.Add(("installing", UpdateToastContent.Installing(version)));
        toasts.Add(("installed", UpdateToastContent.Installed(version)));
        toasts.Add(("failed", UpdateToastContent.Failed(version, earlier, velopack, own)));
        toasts.Add(("broken install", InstallToastContent.Broken()));

        return toasts;
    }

    /// <summary>So many relays of one kind, the first of them carrying the kind's names when they are named.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="state">How many, and whether they are named.</param>
    /// <param name="names">The kind's names, in order.</param>
    /// <returns>The relays.</returns>
    private static IEnumerable<HoldingRelay> RelaysOf(RelayReconnect kind, (int Count, bool Named) state, ConversationName[] names) =>
        Enumerable.Range(0, state.Count).Select(index => new HoldingRelay(
            "Claude Code 2.1.296",
            @"C:\Source\BrowserAI",
            T0.AddMinutes(5),
            CallInFlight: false,
            kind,
            Label: state.Named && index < names.Length ? names[index] : null));

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

    /// <summary>A title as a toast names it: in straight quotes.</summary>
    [GeneratedRegex("\"[^\"]*\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedName();
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The settings every <c>init</c> and <c>resume</c> states, and the call that changes a
/// session's settings, or sets a longer idle time, held back once: F2 of 2026-10-08.
/// </summary>
/// <remarks>
/// <para>
/// <b>F2 d, the maintainer's words of 2026-10-08 verbatim:</b> <i>"f2 d - so we need to
/// store this in the session. Also, explain in the hold text what parameter is different,
/// what the previous values was and what the newly requested value was. The agent can then
/// do 1 of 3 things: request the original/lastrun parameter set with an instant ok, request
/// the same changed parameter set as the last call with an instant ok, or request a new
/// parameter set with a similar single refuse message again."</i> <b>F1 a</b>: on a live
/// session the same call sent again closes the browser and opens it with the new settings
/// itself. <b>F5 a</b>: every answer that opens a visible window ends with one line saying
/// it can go again at no loss.
/// </para>
/// <para>
/// <b>Planted red on 2026-10-08</b> against the batch's own code with each behaviour taken
/// out in turn; the plants are named on each arm.
/// </para>
/// </remarks>
internal sealed class SettingsHoldBackTests
{
    /// <summary>The nominal hidden default; nothing waits for it.</summary>
    private static readonly TimeSpan ShortPeriod = TimeSpan.FromMilliseconds(800);

    /// <summary>A viewport that is not the default, so a call that leaves it out asks for something else.</summary>
    private const string OtherViewport = "1280x720";

    /// <summary>A locale that is not this machine's.</summary>
    private static readonly string OtherLocale = BrowserConfiguration.HostLocale == "de-CH" ? "fr-CH" : "de-CH";

    /// <summary>
    /// A call that leaves out any of the four settings every call states is refused,
    /// naming every one it left out, and creates nothing.
    /// </summary>
    /// <remarks>Planted red by making the four optional again, at their old defaults.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInitOrResumeThatLeavesOutAStatedSettingIsRefusedNamingEveryOneAndCreatesNothing()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var directory = Path.Combine(rig.Root, "four-settings");

        var refused = await CallAsync(harness, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose init leaves two settings out",
            [RunSettingNames.Headed] = false,
            [RunSettingNames.CaptureNetwork] = false,
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).IsEqualTo(SessionErrors.SettingsNotStated(SessionToolSurface.Init, [RunSettingNames.Transcript, IdleSetting.ParameterName]));
        await Assert.That(TextOf(refused)).StartsWith(
            "browserai_init takes 'headed', 'transcript', 'captureNetwork' and 'idleMinutes' on every call, and this one left out 'transcript' and 'idleMinutes'. Nothing was created and nothing was changed.");
        await Assert.That(Directory.Exists(directory)).IsFalse()
            .Because("a refused init created the directory");

        await OpenAsync(harness, directory, Settings(headed: false));

        var resumed = await CallAsync(harness, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "a resume that leaves out whether it wants a window",
            [RunSettingNames.Transcript] = false,
            [RunSettingNames.CaptureNetwork] = false,
            [IdleSetting.ParameterName] = 10,
        });

        await Assert.That((bool?)resumed["isError"]).IsTrue();
        await Assert.That(TextOf(resumed)).IsEqualTo(SessionErrors.SettingsNotStated(SessionToolSurface.Resume, [RunSettingNames.Headed]));
    }

    /// <summary>
    /// At init only a longer idle time is held back, once, with the warning that updates
    /// wait; the same call then goes through, a different one is held again, and a window,
    /// a transcript and a capture at init go through at once.
    /// </summary>
    /// <remarks>Planted red by taking the init's hold-back out.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInitWithALongerIdleTimeIsHeldBackOnceWithTheWarningAndNothingElseAtInitIs()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var longer = Path.Combine(rig.Root, "longer-idle");
        var asked = Settings(headed: false, idle: 240);

        var held = await CallAsync(harness, SessionToolSurface.Init, InitArguments(longer, asked));

        await Assert.That((bool?)held["isError"]).IsTrue();
        await Assert.That(TextOf(held)).IsEqualTo(SessionErrors.LongerIdleHeldBack(Run(asked)));
        await Assert.That(TextOf(held)).StartsWith("Not done yet, and nothing in this call is wrong.");
        await Assert.That(TextOf(held)).Contains(
            "UPDATES WAIT WHILE THIS BROWSER IS OPEN. idleMinutes: 240 is longer than the default: a browser with no window closes after 10 minutes. "
            + "BrowserAI cannot install an update while a session's browser is open, so every update waits until this one closes: after 240 minutes in which no call names the session, or when browserai_close closes it.");
        await Assert.That(TextOf(held)).EndsWith("If you meant it, send exactly the same call again and it will go through. Otherwise send it with idleMinutes: 10 or less.");
        await Assert.That(Directory.Exists(longer)).IsFalse()
            .Because("a held-back init created the directory");

        // A different longer time is a new set, held once again.
        var other = await CallAsync(harness, SessionToolSurface.Init, InitArguments(longer, Settings(headed: false, idle: IdleSetting.NeverWord)));

        await Assert.That((bool?)other["isError"]).IsTrue();
        await Assert.That(TextOf(other)).Contains("idleMinutes: \"never\" means BrowserAI never closes it for being idle, where a browser with no window closes after 10 minutes by default.");

        // The same call as the one held just before goes through.
        _ = await CallAsync(harness, SessionToolSurface.Init, InitArguments(longer, Settings(headed: false, idle: IdleSetting.NeverWord)));

        await Assert.That(Directory.Exists(longer)).IsTrue()
            .Because("the same call sent again after its hold-back did not go through");

        // A window, a transcript and a capture at their mode's default time go through at once.
        var everything = Path.Combine(rig.Root, "everything-at-init");
        var opened = await CallAsync(harness, SessionToolSurface.Init, InitArguments(everything, Settings(headed: true, transcript: true, captureNetwork: true, idle: 60)));

        await Assert.That((bool?)opened["isError"]).IsFalse()
            .Because("an init was held back for something other than a longer idle time");
    }

    /// <summary>
    /// A resume whose settings differ from the last run's is held back once, naming each
    /// difference with both values and marking a left-out one as its default; the same call
    /// then goes through, and a new set is held once again.
    /// </summary>
    /// <remarks>Planted red by comparing nothing, and by holding back every call.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AResumeThatChangesSettingsIsHeldBackOnceNamingEachDifferenceAndTheSameCallGoesThrough()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var directory = Path.Combine(rig.Root, "changed-settings");
        var first = Settings(headed: false, viewport: OtherViewport, locale: OtherLocale);

        await OpenAsync(harness, directory, first);
        _ = await NavigateAsync(harness, directory, "the call that starts the browser");
        _ = await CallAsync(harness, SessionToolSurface.Close, new JsonObject { ["session"] = directory, ["why"] = "the suite closing the browser before it resumes" });

        var asked = Settings(headed: true, idle: 60);
        var held = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(directory, asked));

        await Assert.That((bool?)held["isError"]).IsTrue();
        await Assert.That(TextOf(held)).IsEqualTo(SessionErrors.SettingsHeldBack(
            [
                new SettingDifference(RunSettingNames.Headed, "false", "true", LeftOut: false),
                new SettingDifference(IdleSetting.ParameterName, "10", "60", LeftOut: false),
                new SettingDifference(RunSettingNames.Viewport, $"'{OtherViewport}'", $"'{BrowserConfiguration.DefaultViewport}'", LeftOut: true),
                new SettingDifference(RunSettingNames.Locale, $"'{OtherLocale}'", $"'{BrowserConfiguration.HostLocale}'", LeftOut: true),
            ],
            Run(first),
            Run(asked),
            ResumeFinds.NotLive,
            countdownStarted: false));

        // And the sentences themselves, because a text compared only with the method that
        // writes it cannot tell a true sentence from a false one.
        var text = TextOf(held);

        await Assert.That(text).StartsWith("Not done yet, and nothing in this call is wrong. It asks for settings that differ from the ones this session's last run used, and BrowserAI holds back such a call once, so that a change is a choice and not an accident.\nWhat differs:\n");
        await Assert.That(text).Contains("- headed: the last run had false, this call asks for true\n");
        await Assert.That(text).Contains($"- viewport: the last run had '{OtherViewport}', this call asks for '{BrowserConfiguration.DefaultViewport}' (the default, because the call left it out)\n");
        await Assert.That(text).Contains("If you meant it, send exactly the same call again and it will go through, and the session opens with these settings.\n");
        await Assert.That(text).EndsWith($"To keep the last run's settings, send them instead: headed: false, transcript: false, captureNetwork: false, idleMinutes: 10, viewport: '{OtherViewport}', locale: '{OtherLocale}'.");
        await Assert.That(text).DoesNotContain("UPDATES WAIT")
            .Because("an hour for a visible window is its default, and the warning is for a longer time");

        // The same call goes through, with a window.
        var opened = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(directory, asked));

        await Assert.That((bool?)opened["isError"]).IsFalse();
        await Assert.That(TextOf(opened)).Contains("  headed: true -- a window is open for this run\n");

        // A new set is held once again.
        var again = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(directory, Settings(headed: false)));

        await Assert.That((bool?)again["isError"]).IsTrue();
        await Assert.That(TextOf(again)).Contains("- headed: the last run had true, this call asks for false\n");
        await Assert.That(TextOf(again)).Contains("the session's browser has not started yet, so the first browser call after it starts the browser with these settings.");
    }

    /// <summary>
    /// On a live session whose browser is up, the held call changes nothing and starts the
    /// countdown again; the same call sent again closes the browser cleanly, records why,
    /// and opens it with the new settings.
    /// </summary>
    /// <remarks>Planted red by tearing the session down without the clean close.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALiveSessionsBrowserIsClosedCleanlyAndOpenedWithTheNewSettingsWhenTheSameCallIsSentAgain()
    {
        var clock = new ManualClock();

        await using var rig = Sessions(clock);
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var directory = Path.Combine(rig.Root, "switched");

        await OpenAsync(harness, directory, Settings(headed: false));
        _ = await NavigateAsync(harness, directory, "the call that starts the browser");

        var before = rig.SessionChildren[^1];
        var asked = Settings(headed: true, idle: 60);

        clock.Advance(ShortPeriod / 2);

        var held = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(directory, asked));

        await Assert.That((bool?)held["isError"]).IsTrue();
        await Assert.That(TextOf(held)).Contains("If you meant it, send exactly the same call again and it will go through: the session's browser then closes and opens again with these settings, and its logins, cookies, storage, tabs and history are kept.\n");
        await Assert.That(TextOf(held)).EndsWith("\nThis call started the session's idle countdown again, as every call that names it does.");

        // Nothing changed: the same browser, no close sent, and the countdown moved.
        await Assert.That(before.HasStopped).IsFalse();
        await Assert.That(before.ToolCallsReceived).DoesNotContain(LiveSession.BrowserCloseTool);
        await Assert.That(harness.Proxy.SessionCountdowns().Single().ClosesAt).IsEqualTo(clock.GetUtcNow() + ShortPeriod)
            .Because("a held-back call named the live session and did not start its countdown again");

        var switched = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(directory, asked));

        await Assert.That((bool?)switched["isError"]).IsFalse();
        await Assert.That(before.ToolCallsReceived).Contains(LiveSession.BrowserCloseTool)
            .Because("the browser was not asked to close itself before the session was opened again");
        await WaitUntilAsync(() => before.HasStopped, "the old browser server was never ended");

        var after = rig.SessionChildren[^1];

        await Assert.That(after).IsNotSameReferenceAs(before);
        await Assert.That((bool?)ConfigOf(rig.Launches[^1])["browser"]?["launchOptions"]?["headless"]).IsFalse();

        var record = SessionLock.ReadRecord(SessionPath.For(directory))!;

        await Assert.That(record.ClosedHistory[^1].Value.Cause).IsEqualTo(SessionCloseCause.SettingsChanged);
        await Assert.That(record.SettingsHistory[^1].Value).IsEqualTo(Run(asked).Write());

        var text = TextOf(switched);

        await Assert.That(text).Contains("to open again with the settings a browserai_resume call from this client asked for, which gave the reason \"the suite resuming with other settings\".");
        await Assert.That(text.TrimEnd('\n')).EndsWith(SettingsHoldBack.HeadedHint);
    }

    /// <summary>
    /// The last run's settings, written as the hold-back writes them, go through with no
    /// hold-back: on a live session that is the already-live answer.
    /// </summary>
    /// <remarks>Planted red by leaving the optional settings out of the list the hold-back writes.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLastRunsSettingsAsTheHoldBackWritesThemGoThroughAtOnce()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var directory = Path.Combine(rig.Root, "kept-as-it-was");
        var first = Settings(headed: false, viewport: OtherViewport);

        await OpenAsync(harness, directory, first);
        _ = await NavigateAsync(harness, directory, "the call that starts the browser");

        var held = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(directory, Settings(headed: false)));

        await Assert.That(TextOf(held)).EndsWith($"To keep the last run's settings, send them instead: headed: false, transcript: false, captureNetwork: false, idleMinutes: 10, viewport: '{OtherViewport}'.\nThis call started the session's idle countdown again, as every call that names it does.");

        // Exactly that list, as a call.
        var kept = await CallAsync(harness, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "the suite keeping the last run's settings",
            [RunSettingNames.Headed] = false,
            [RunSettingNames.Transcript] = false,
            [RunSettingNames.CaptureNetwork] = false,
            [IdleSetting.ParameterName] = 10,
            [RunSettingNames.Viewport] = OtherViewport,
        });

        await Assert.That((bool?)kept["isError"]).IsFalse();
        await Assert.That(TextOf(kept)).StartsWith(SessionManager.AlreadyLive(browserUp: true, purposeChanged: false));
    }

    /// <summary>
    /// The same changed call goes through only on the connection it was held back on:
    /// another connection meets its own hold-back first.
    /// </summary>
    /// <remarks>Planted red by sharing one memory between every connection.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSameChangedCallGoesThroughOnlyOnTheConnectionThatWasHeldBack()
    {
        await using var sessions = Sessions(new ManualClock());
        await using var host = await SessionHostRig.StartAsync(sessions);

        var first = await host.ConnectAsync("the first client");
        var directory = Path.Combine(sessions.Root, "two-connections");
        var asked = Settings(headed: true, idle: 60);

        await OpenAsync(first, directory, Settings(headed: false));
        _ = await first.CallAsync("browser_navigate", NavigateArguments(directory, "the call that starts the browser"));

        var heldOnFirst = await first.CallAsync(SessionToolSurface.Resume, ResumeArguments(directory, asked));

        await Assert.That((bool?)heldOnFirst["isError"]).IsTrue();

        // The first client goes with the browser up, so the session is kept for the next.
        await first.EndAsync();

        var second = await host.ConnectAsync("the second client");
        var heldOnSecond = await second.CallAsync(SessionToolSurface.Resume, ResumeArguments(directory, asked));

        await Assert.That((bool?)heldOnSecond["isError"]).IsTrue()
            .Because("a call held back on one connection went through on another");
        await Assert.That(HostConnection.TextOf(heldOnSecond)).StartsWith(SettingsHoldBack.NothingIsWrong);

        var through = await second.CallAsync(SessionToolSurface.Resume, ResumeArguments(directory, asked));

        await Assert.That((bool?)through["isError"]).IsFalse();
        await Assert.That(HostConnection.TextOf(through)).Contains("  headed: true -- a window is open for this run\n");
    }

    /// <summary>
    /// A session nobody holds is compared with the settings its record keeps: one row per
    /// change, none for a resume that changes nothing.
    /// </summary>
    /// <remarks>Planted red by writing no settings statement at the opening.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionNobodyHoldsIsComparedWithTheSettingsItsRecordKeeps()
    {
        await using var sessions = Sessions(new ManualClock());
        await using var host = await SessionHostRig.StartAsync(sessions);

        var first = await host.ConnectAsync("the client that made the session");
        var directory = Path.Combine(sessions.Root, "kept-on-disk");
        var made = Settings(headed: false, viewport: OtherViewport);

        await OpenAsync(first, directory, made);

        // No browser was up, so the session is let go when its client goes.
        await first.EndAsync();
        await WaitUntilAsync(() => host.Host.Sessions.Find(directory) is null, "the session was never let go");

        var record = SessionLock.ReadRecord(SessionPath.For(directory))!;

        await Assert.That(record.SettingsHistory.Count).IsEqualTo(1);
        await Assert.That(record.SettingsHistory[0].Value).IsEqualTo(Run(made).Write());

        var second = await host.ConnectAsync("a client that did not make it");
        var asked = Settings(headed: false);
        var held = await second.CallAsync(SessionToolSurface.Resume, ResumeArguments(directory, asked));

        await Assert.That((bool?)held["isError"]).IsTrue();
        await Assert.That(HostConnection.TextOf(held)).IsEqualTo(SessionErrors.SettingsHeldBack(
            [new SettingDifference(RunSettingNames.Viewport, $"'{OtherViewport}'", $"'{BrowserConfiguration.DefaultViewport}'", LeftOut: true)],
            Run(made),
            Run(asked),
            ResumeFinds.NotLive,
            countdownStarted: false));

        _ = await second.CallAsync(SessionToolSurface.Resume, ResumeArguments(directory, asked));

        // And a resume that changes nothing writes no row.
        var same = await second.CallAsync(SessionToolSurface.Resume, ResumeArguments(directory, asked));

        await Assert.That((bool?)same["isError"]).IsFalse();

        var after = SessionLock.ReadRecord(SessionPath.For(directory))!;

        await Assert.That(after.SettingsHistory.Count).IsEqualTo(2);
        await Assert.That(after.SettingsHistory[^1].Value).IsEqualTo(Run(asked).Write());
    }

    /// <summary>
    /// A session whose record keeps no settings, written before they were kept, is treated
    /// as an init: only a longer idle time is held back.
    /// </summary>
    /// <remarks>Planted red by comparing such a session with the defaults.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionWhoseRecordKeepsNoSettingsIsHeldBackOnlyForALongerIdleTime()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var older = LegacySession(rig, "written-before-settings");
        var longer = LegacySession(rig, "written-before-settings-longer");

        var window = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(older.FullPath, Settings(headed: true, transcript: true, idle: 60, viewport: OtherViewport)));

        await Assert.That((bool?)window["isError"]).IsFalse()
            .Because("a session with no recorded settings was held back for a difference it cannot know of");

        var asked = Settings(headed: false, idle: 240);
        var held = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(longer.FullPath, asked));

        await Assert.That((bool?)held["isError"]).IsTrue();
        await Assert.That(TextOf(held)).IsEqualTo(SessionErrors.LongerIdleHeldBack(Run(asked)));

        var through = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(longer.FullPath, asked));

        await Assert.That((bool?)through["isError"]).IsFalse();
    }

    /// <summary>
    /// A longer idle time the last run already had goes through with no warning, a
    /// different longer one is held with it, and so is a switch to no window that keeps a
    /// window's hour.
    /// </summary>
    /// <remarks>Planted red by holding back every call whose idle time is longer than its default.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALongerIdleTimeTheLastRunAlreadyHadGoesThroughWithNoWarning()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var directory = Path.Combine(rig.Root, "kept-longer");
        var longer = Settings(headed: false, idle: 240);

        _ = await CallAsync(harness, SessionToolSurface.Init, InitArguments(directory, longer));
        await OpenAsync(harness, directory, longer);

        var repeated = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(directory, longer));

        await Assert.That((bool?)repeated["isError"]).IsFalse()
            .Because("a longer time identical to the last run was held back again");
        await Assert.That(TextOf(repeated)).DoesNotContain("UPDATES WAIT");

        var never = Settings(headed: false, idle: IdleSetting.NeverWord);
        var held = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(directory, never));

        await Assert.That((bool?)held["isError"]).IsTrue();
        await Assert.That(TextOf(held)).Contains(SettingsHoldBack.UpdatesWait(Run(never)));

        // A visible window's hour, kept for a browser with no window.
        var visible = Path.Combine(rig.Root, "visible-hour");

        await OpenAsync(harness, visible, Settings(headed: true, idle: 60));

        var hiddenHour = Settings(headed: false, idle: 60);
        var switched = await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(visible, hiddenHour));

        await Assert.That((bool?)switched["isError"]).IsTrue();
        await Assert.That(TextOf(switched)).Contains(
            "UPDATES WAIT WHILE THIS BROWSER IS OPEN. idleMinutes: 60 is longer than the default: a browser with no window closes after 10 minutes.");
    }

    /// <summary>
    /// Every answer that opens a visible window ends with the line that says it can go again
    /// at no loss, and no other answer carries it.
    /// </summary>
    /// <remarks>Planted red by leaving the line off.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryAnswerThatOpensAVisibleWindowEndsWithTheHintAndNoOtherDoes()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var visible = Path.Combine(rig.Root, "with-a-window");
        var hidden = Path.Combine(rig.Root, "without-a-window");

        var opened = TextOf(await CallAsync(harness, SessionToolSurface.Init, InitArguments(visible, Settings(headed: true, idle: 60))));
        var openedHidden = TextOf(await CallAsync(harness, SessionToolSurface.Init, InitArguments(hidden, Settings(headed: false))));
        var alreadyLive = TextOf(await CallAsync(harness, SessionToolSurface.Resume, ResumeArguments(visible, Settings(headed: true, idle: 60))));

        await Assert.That(opened.TrimEnd('\n')).EndsWith("\nWhen the part that needs the person is done, resuming with headed: false keeps everything.");
        await Assert.That(openedHidden).DoesNotContain(SettingsHoldBack.HeadedHint);
        await Assert.That(alreadyLive).StartsWith(SessionManager.AlreadyLive(browserUp: false, purposeChanged: false));
        await Assert.That(alreadyLive).DoesNotContain(SettingsHoldBack.HeadedHint)
            .Because("an answer that opened no window carried the line");
    }

    /// <summary>
    /// The four settings are required in both schemas, the window's description says what it
    /// costs and that switching keeps everything, and the idle description names both
    /// defaults and the hold-back.
    /// </summary>
    /// <remarks>Planted red by taking the cost and the fact out of the description.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSchemasRequireTheFourAndTheWindowsDescriptionSaysWhatItCostsAndWhatSwitchingKeeps()
    {
        await using var rig = Sessions(new ManualClock());
        await using var harness = await McpTestHarness.ThroughTheProxyAsync(sessions: rig);

        var listed = await harness.Client.RoundTripAsync("tools/list");
        var tools = (listed["tools"]?.AsArray() ?? []).OfType<JsonObject>().ToDictionary(tool => (string)tool["name"]!, StringComparer.Ordinal);

        foreach (var name in new[] { SessionToolSurface.Init, SessionToolSurface.Resume })
        {
            var schema = tools[name]["inputSchema"]!;
            var required = (schema["required"]?.AsArray() ?? []).Select(entry => (string?)entry).ToList();

            foreach (var stated in RunSettingNames.Stated)
            {
                await Assert.That(required).Contains(stated)
                    .Because($"{name} does not require '{stated}'");
            }

            var headed = (string)schema["properties"]![RunSettingNames.Headed]!["description"]!;

            await Assert.That(headed).Contains("A VISIBLE WINDOW TAKES THE PERSON'S SCREEN AND FOCUS: Chromium's comes to the front and takes the keyboard focus when it opens, and Firefox's may, and like any open browser it holds BrowserAI's updates back until it closes.");
            await Assert.That(headed).Contains("Switching between visible and hidden keeps logins, cookies, storage, tabs and history");

            var idle = (string)schema["properties"]![IdleSetting.ParameterName]!["description"]!;

            await Assert.That(idle).Contains("The defaults are 10 minutes without a window and 60 with one.");
            await Assert.That(idle).Contains("a call that sets one is held back once with that warning.");

            // And every description the two tools carry fits what a client hands a
            // model whole, which the published binary's own arm asserts off the wire.
            foreach (var description in new[] { (string)tools[name]["description"]! }
                .Concat((schema["properties"]?.AsObject() ?? []).Select(property => (string)property.Value!["description"]!)))
            {
                await Assert.That(description.Length).IsLessThanOrEqualTo(ClientTruncationBudget.Characters)
                    .Because($"a description on {name} is past the client's budget");
            }
        }
    }

    /// <summary>
    /// A run's settings read back from the record as the same settings, never and an unknown
    /// time zone included, and a value that is not all of them reads back as nothing.
    /// </summary>
    /// <remarks>Planted red by reading every value back as nothing.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStoredRunReadsBackAsTheSameSettingsAndAnUnreadableOneAsNothing()
    {
        var full = new SessionRunSettings(
            Headed: true,
            Transcript: true,
            Debug: true,
            new RunOptions { Viewport = new ViewportSize(1280, 720), Locale = "de-CH", TimeZone = null, IgnoreHttpsErrors = true, CaptureNetwork = true },
            IdleSetting.Never);
        var plain = new SessionRunSettings(false, false, false, RunOptions.Default, IdleSetting.Of(10));

        await Assert.That(SessionRunSettings.Read(full.Write())).IsEqualTo(full);
        await Assert.That(SessionRunSettings.Read(plain.Write())).IsEqualTo(plain);

        var partial = JsonNode.Parse(plain.Write())!.AsObject();

        _ = partial.Remove(RunSettingNames.Locale);

        await Assert.That(SessionRunSettings.Read(partial.ToJsonString())).IsNull();
        await Assert.That(SessionRunSettings.Read("not json")).IsNull();
        await Assert.That(SessionRunSettings.Read("[]")).IsNull();
    }

    /// <summary>The settings one arm asks for, as the call writes them.</summary>
    private sealed record AskedSettings(bool Headed, bool Transcript, bool CaptureNetwork, JsonNode Idle, string? Viewport, string? Locale);

    private static AskedSettings Settings(bool headed, bool transcript = false, bool captureNetwork = false, JsonNode? idle = null, string? viewport = null, string? locale = null) =>
        new(headed, transcript, captureNetwork, idle ?? (headed ? 60 : 10), viewport, locale);

    /// <summary>What the product makes of an arm's settings, every left-out one at its default.</summary>
    private static SessionRunSettings Run(AskedSettings asked) =>
        new(
            asked.Headed,
            asked.Transcript,
            Debug: false,
            new RunOptions
            {
                Viewport = asked.Viewport is { } viewport && ViewportSize.TryParse(viewport, out var size) ? size : BrowserConfiguration.DefaultViewport,
                Locale = asked.Locale ?? BrowserConfiguration.HostLocale,
                TimeZone = BrowserConfiguration.HostTimeZone,
                CaptureNetwork = asked.CaptureNetwork,
            },
            asked.Idle.GetValueKind() is System.Text.Json.JsonValueKind.String ? IdleSetting.Never : IdleSetting.Of((int)asked.Idle));

    private static JsonObject InitArguments(string directory, AskedSettings asked) =>
        WithSettings(new JsonObject { ["directory"] = directory, ["purpose"] = "a session whose settings the suite changes" }, asked);

    private static JsonObject ResumeArguments(string directory, AskedSettings asked) =>
        WithSettings(new JsonObject { ["directory"] = directory, ["why"] = "the suite resuming with other settings" }, asked);

    private static JsonObject WithSettings(JsonObject arguments, AskedSettings asked)
    {
        arguments[RunSettingNames.Headed] = asked.Headed;
        arguments[RunSettingNames.Transcript] = asked.Transcript;
        arguments[RunSettingNames.CaptureNetwork] = asked.CaptureNetwork;
        arguments[IdleSetting.ParameterName] = asked.Idle.DeepClone();

        if (asked.Viewport is { } viewport)
        {
            arguments[RunSettingNames.Viewport] = viewport;
        }

        if (asked.Locale is { } locale)
        {
            arguments[RunSettingNames.Locale] = locale;
        }

        return arguments;
    }

    private static async Task OpenAsync(McpTestHarness harness, string directory, AskedSettings asked)
    {
        var answer = await CallAsync(harness, SessionToolSurface.Init, InitArguments(directory, asked));

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The arm could not open '{directory}': {TextOf(answer)}");
        }
    }

    private static async Task OpenAsync(HostConnection connection, string directory, AskedSettings asked)
    {
        var answer = await connection.CallAsync(SessionToolSurface.Init, InitArguments(directory, asked));

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The arm could not open '{directory}': {HostConnection.TextOf(answer)}");
        }
    }

    /// <summary>A session directory whose record was written the way a build before 2026-10-08 wrote it: with no settings.</summary>
    private static SessionPath LegacySession(RigSessionEnvironment rig, string name)
    {
        var path = SessionPath.For(Path.Combine(rig.Root, name));

        SessionLayout.Create(path);

        using (SessionLock.TryAcquire(path, new SessionLockRequest { Browser = SessionManager.DefaultBrowser, Purpose = "a session from before settings were kept" }, NullLogger.Instance).Acquired!)
        {
        }

        return path;
    }

    private static Task<JsonObject> NavigateAsync(McpTestHarness harness, string directory, string why) =>
        CallAsync(harness, "browser_navigate", NavigateArguments(directory, why));

    private static JsonObject NavigateArguments(string directory, string why) => new()
    {
        ["session"] = directory,
        ["why"] = why,
        ["url"] = "data:text/html,<h1>ok</h1>",
    };

    private static async Task<JsonObject> CallAsync(McpTestHarness harness, string tool, JsonObject arguments) =>
        await harness.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

    private static JsonObject ConfigOf(BrowserAI.Protocol.ChildProcessOptions launch)
    {
        var file = launch.Arguments[launch.Arguments.ToList().IndexOf("--config") + 1];

        return JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    }

    private static RigSessionEnvironment Sessions(ManualClock clock) =>
        RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools["browser_snapshot"] = new FakeToolBehaviour();
                child.Tools[LiveSession.BrowserCloseTool] = new FakeToolBehaviour();
            },
            opensDefaultSession: false,
            browserIdlePeriod: ShortPeriod,
            clock: clock);

    /// <summary>Waits for an event with the clock still, bounded by the suite's hang detector.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string whatWentWrong)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > TestDefaults.InProcessHang)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds:F1} s.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}

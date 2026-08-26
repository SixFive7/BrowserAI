// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The attribution half of the stray sweep: the message-only window walk, and
/// the undocumented read the whole thing rests on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every test here runs in milliseconds and needs no browser</b>, which is
/// deliberate: the property under test is a property of Windows rather than of
/// Chromium, and pinning it to a browser launch would make it something nobody
/// runs. The window that stands in for a browser is published by a probe
/// process, because window classes are per-process and any program may register
/// <c>Chrome_MessageWindow</c> — which is simultaneously how this is testable
/// and why attribution is never allowed to decide anything on its own.
/// </para>
/// <para>
/// <b>Re-verification rows 4, 4a and 4c live here</b>
/// ([kb](../../kb/re-verification.md)): the title format and the
/// exact-title probe's canonicalisation rules, the cross-process
/// <c>WM_GETTEXT</c> bypass, and the window walk that finds a titled window
/// owned by a pid we name.
/// </para>
/// </remarks>
internal sealed class MessageWindowTests
{
    /// <summary>
    /// The class a real Chromium publishes, taken from the product so a rename
    /// there cannot leave these tests measuring a class nothing uses.
    /// </summary>
    private static string SingletonClass => MessageWindows.ChromiumSingletonClass;

    [Test]
    public async Task ACrossProcessReadGetsTheRealNameFromAWindowThatSuppressesWmGetText()
    {
        using var scratch = ScratchDirectory.Create("window-bypass");
        using var scope = new JobObjectScope();

        // A GUID, so the string this test reads back cannot have come from
        // anywhere else on the machine.
        var name = $"BrowserAI-bypass-{Guid.NewGuid():D}";

        var report = await PlantedProbe.PublishWindowAsync(
            scope,
            ProbeExecutable,
            scratch.Path,
            className: $"BrowserAI_Probe_{Guid.NewGuid():N}",
            title: name,
            suppress: true);

        var window = (nint)(long)report["window"]!;

        // The probe's own reads, from inside the owning process. Both go through
        // the WndProc, and the WndProc lies -- so both are empty. This is the
        // half that makes the cross-process answer below evidence rather than a
        // tautology: without it, a window that simply answered normally would
        // pass this test.
        await Assert.That((string?)report["sameProcessGetWindowText"]).IsEmpty();
        await Assert.That((string?)report["sameProcessSendMessage"]).IsEmpty();
        await Assert.That((bool?)report["suppressing"]).IsTrue();

        var crossProcess = MessageWindows.WindowText(window);

        // ⚠️ THE ANSWER IS THE ASSERTION, and it is why the stopwatch that used
        // to be here is gone.
        //
        // Deleted 2026-08-18: `Assert.That(elapsed).IsLessThan(250 ms)`, whose
        // note read "a read that started costing a timeout would mean it had
        // started going through the message queue". The window under test
        // SUPPRESSES WM_GETTEXT -- the two same-process reads above are asserted
        // empty for exactly that reason -- so a read that went through the
        // message queue would time out and come back EMPTY. Getting the name at
        // all is therefore proof that the queue was bypassed, whatever the clock
        // said. Two hundred and fifty milliseconds, meanwhile, is a number a
        // starved machine reaches while the product is behaving perfectly.
        await Assert.That(crossProcess).IsEqualTo(name);

        // The documented fallback agrees, which is the only reason it is worth
        // carrying at all.
        await Assert.That(MessageWindows.InternalWindowText(window)).IsEqualTo(name);
        await Assert.That(MessageWindows.TitleOf(window)).IsEqualTo(name);
    }

    /// <summary>
    /// The two title APIs agree about every message window on this machine —
    /// where "disagree" means the two <i>APIs</i> disagree, not that the window
    /// changed between two reads of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It flaked once in five full runs on 2026-08-26, and the flake was
    /// the test measuring something other than its own claim.</b>
    /// <c>GetWindowTextW</c> answered a title for a Chromium window while
    /// <c>InternalGetWindowText</c> answered empty for the same handle — the
    /// suite's own concurrent slice session tearing its browser down between the
    /// two calls. <b>A window that vanished or was renamed between two reads is
    /// not an API disagreement</b>, and the old shape could not tell the two
    /// apart because it read each handle exactly once per API.
    /// </para>
    /// <para>
    /// <b>The fix is a second probe in the reverse order, and it is a
    /// discriminator rather than a retry.</b> The four reads are
    /// <c>documented, fallback, fallback, documented</c>: a genuine API
    /// disagreement is <i>stable</i>, so both APIs answer the same thing twice
    /// and the pair still differs; a window moving under the read changes at
    /// least one of the two, which is a fact about the window and is counted as
    /// one. <b>It is not a retry-until-green loop:</b> there is no loop, no
    /// clock, and a stable disagreement fails on the second probe exactly as it
    /// did on the first. It is also not a weakening — the assertion is still
    /// <i>zero divergences</i>, over strictly more evidence per window.
    /// </para>
    /// <para>
    /// <b>The positive control is our own window, and it is what stops the
    /// discriminator swallowing everything.</b> A planted window that nothing is
    /// tearing down must read stably on all four calls; an implementation that
    /// classified every divergence as movement would still have to produce a
    /// stable named window here, and the count of moving windows is reported
    /// beside any failure rather than hidden.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheTwoTitleApisAgreeOnEveryMessageWindowOnThisMachine()
    {
        using var scratch = ScratchDirectory.Create("window-agreement");
        using var scope = new JobObjectScope();

        // One window of our own, so the comparison can never be vacuous on a
        // machine that happens to be running nothing.
        var name = $@"C:\BrowserAI-agreement-{Guid.NewGuid():N}";
        _ = await PlantedProbe.PublishWindowAsync(scope, ProbeExecutable, scratch.Path, SingletonClass, name);

        var walk = MessageWindows.Walk(SingletonClass);
        var divergences = new List<string>();
        var moved = new List<string>();
        var named = 0;

        foreach (var window in walk.Windows)
        {
            var documented = MessageWindows.WindowText(window.Handle);
            var fallback = MessageWindows.InternalWindowText(window.Handle);

            if (documented.Length is not 0)
            {
                named++;
            }

            if (string.Equals(documented, fallback, StringComparison.Ordinal))
            {
                continue;
            }

            // ⚠️ REVERSED ON PURPOSE. Reading the same API first both times
            // would let a window that is changing monotonically -- a title being
            // cleared during teardown -- look stable to whichever call happened
            // to run after the change.
            var fallbackAgain = MessageWindows.InternalWindowText(window.Handle);
            var documentedAgain = MessageWindows.WindowText(window.Handle);

            var stable = string.Equals(documented, documentedAgain, StringComparison.Ordinal)
                && string.Equals(fallback, fallbackAgain, StringComparison.Ordinal);

            var evidence = $"{window.Handle:X}: GetWindowTextW='{documented}'/'{documentedAgain}' InternalGetWindowText='{fallback}'/'{fallbackAgain}'";

            if (stable)
            {
                divergences.Add(evidence);
            }
            else
            {
                moved.Add(evidence);
            }
        }

        await Assert.That(string.Join(Environment.NewLine, divergences)).IsEmpty()
            .Because($"{moved.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} window(s) changed under the read and were not counted as API disagreements");

        await Assert.That(walk.Truncated).IsFalse();

        // Not vacuous in either direction: the walk found windows, and at least
        // one of them was named -- ours.
        await Assert.That(walk.Windows.Count).IsGreaterThan(0);
        await Assert.That(named).IsGreaterThan(0);

        // ⚠️ THE POSITIVE CONTROL FOR THE DISCRIMINATOR. Our own window is not
        // being torn down, so all four reads of it agree -- which is what a
        // window the two APIs really do agree about looks like, and what an
        // implementation that classified every divergence as movement could not
        // produce.
        var ours = walk.Windows
            .Where(window => string.Equals(MessageWindows.WindowText(window.Handle), name, StringComparison.Ordinal))
            .ToList();

        await Assert.That(ours.Count).IsEqualTo(1);
        await Assert.That(MessageWindows.InternalWindowText(ours[0].Handle)).IsEqualTo(name);
        await Assert.That(MessageWindows.WindowText(ours[0].Handle)).IsEqualTo(name);
        await Assert.That(MessageWindows.InternalWindowText(ours[0].Handle)).IsEqualTo(name);
    }

    [Test]
    public async Task EnumWindowsFindsNoMessageWindowsAtAllWhileTheWalkFindsThem()
    {
        using var scratch = ScratchDirectory.Create("window-enumwindows");
        using var scope = new JobObjectScope();

        var name = $@"C:\BrowserAI-enumwindows-{Guid.NewGuid():N}";
        var report = await PlantedProbe.PublishWindowAsync(scope, ProbeExecutable, scratch.Path, SingletonClass, name);

        // Two windows in one probe process, differing in one thing: the
        // message-only one has HWND_MESSAGE as its parent and the control has
        // none. Everything below is about that difference.
        var messageOnly = (nint)(long)report["window"]!;
        var control = (nint)(long)report["topLevelWindow"]!;
        var controlClass = (string?)report["topLevelClassName"];

        var topLevel = TopLevelWindows.All();
        var throughEnumWindows = topLevel.Count(window =>
            string.Equals(TopLevelWindows.ClassNameOf(window), SingletonClass, StringComparison.Ordinal));

        var walk = MessageWindows.Walk(SingletonClass);

        // THE POINT: the obvious-looking simplification -- enumerate every
        // window and filter by class -- finds NOTHING, on a machine that has
        // dozens. It would not throw and it would not warn; every sweep would
        // report a clean machine forever.
        await Assert.That(throughEnumWindows).IsEqualTo(0);
        await Assert.That(walk.Windows.Count).IsGreaterThan(0);
        await Assert.That(walk.Windows.Any(found => found.Handle == messageOnly)).IsTrue();

        // ⚠️ THE POSITIVE CONTROL, and it is by HANDLE IDENTITY rather than by
        // population.
        //
        // Corrected 2026-08-18 (previously `Assert.That(topLevel.Count)
        // .IsGreaterThan(50)`, "the question was not vacuous: EnumWindows really
        // did enumerate"). That floor asserted the developer's screen was busy.
        // It is a [MACHINE] property -- kb/windows/detection.md says so, at 590,
        // 586 and 587 windows across three sweeps of one desktop -- and a CI
        // agent with no interactive desktop has a service window station holding
        // a handful, so the assertion would have gone red on a machine where
        // nothing was wrong.
        //
        // The replacement asks the question the zero above needs answered: could
        // this enumeration have found a window of a class we just registered, if
        // there had been one? The probe publishes a second window that is
        // top-level and never shown, and EnumWindows must return exactly that
        // one and never the message-only one. Both are in the same process, both
        // were created seconds ago, and the answer depends on nothing but the
        // parent each was given.
        await Assert.That(topLevel.Contains(control)).IsTrue();
        await Assert.That(topLevel.Contains(messageOnly)).IsFalse();

        // And the class filter that produced the zero above is not itself broken:
        // read through the same call, our control window carries the class the
        // probe registered. Exactly one, because the probe puts a GUID in that
        // class name -- several tests publish a probe of the singleton class at
        // once and a count without it would be a count of live probes.
        await Assert.That(topLevel.Count(window =>
            string.Equals(TopLevelWindows.ClassNameOf(window), controlClass, StringComparison.Ordinal))).IsEqualTo(1);
    }

    [Test]
    public async Task TheExactTitleProbeMatchesOnlyTheSpellingWindowsItselfMatches()
    {
        using var scratch = ScratchDirectory.Create("window-exact-title");
        using var scope = new JobObjectScope();

        // A path-shaped title, because that is what a browser publishes and the
        // canonicalisation rules below are about paths.
        var directory = Path.Combine(scratch.Path, $"profile-{Guid.NewGuid():N}");
        var report = await PlantedProbe.PublishWindowAsync(scope, ProbeExecutable, scratch.Path, SingletonClass, directory);
        var expected = (nint)(long)report["window"]!;

        // Row 4's canonicalisation table, re-measured rather than carried over.
        await Assert.That(MessageWindows.FindExactly(SingletonClass, directory)).IsEqualTo(expected);
        await Assert.That(MessageWindows.FindExactly(SingletonClass, directory.ToUpperInvariant())).IsEqualTo(expected);
        await Assert.That(MessageWindows.FindExactly(SingletonClass, char.ToLowerInvariant(directory[0]) + directory[1..])).IsEqualTo(expected);
        await Assert.That(MessageWindows.FindExactly(SingletonClass, directory + '\\')).IsEqualTo(nint.Zero);
        await Assert.That(MessageWindows.FindExactly(SingletonClass, directory.Replace('\\', '/'))).IsEqualTo(nint.Zero);
        await Assert.That(MessageWindows.FindExactly(SingletonClass, directory + "x")).IsEqualTo(nint.Zero);

        // The class is mandatory. A walk that dropped it would find nothing,
        // silently -- which is the same failure EnumWindows produces above.
        await Assert.That(MessageWindows.FindExactly("BrowserAI_NoSuchClass", directory)).IsEqualTo(nint.Zero);

        // And the pid the window resolves to is the probe's own, which is what
        // ties a browser to a profile at all.
        await Assert.That(MessageWindows.ProcessIdOf(expected)).IsEqualTo((int)report["pid"]!);
    }

    [Test]
    public async Task TheWalkKeepsFindingAStableWindowWhileOtherWindowsAreDestroyedUnderIt()
    {
        using var scratch = ScratchDirectory.Create("window-churn");
        using var stable = new JobObjectScope();

        var name = $@"C:\BrowserAI-stable-{Guid.NewGuid():N}";
        _ = await PlantedProbe.PublishWindowAsync(stable, ProbeExecutable, scratch.Path, SingletonClass, name);

        var restarts = 0;
        var walks = 0;
        var lost = new List<string>();

        // Windows of the same class appearing and vanishing under the walk is
        // exactly what a machine full of exiting browsers looks like, and it is
        // the condition under which an unchecked walk under-reports.
        //
        // ⚠️ A fixed, small number of rounds rather than a wall-clock window,
        // and that is a cost decision made once: every round starts and kills a
        // process, this test is not serialised against the rest of the suite,
        // and the suite's in-process rigs assert a two-second budget. A longer
        // run buys nothing — a walk either survives a window dying under it or
        // it does not.
        for (var round = 0; round < 3; round++)
        {
            check("during churn", await WalkBesideOneMoreWindowAsync(scratch.Path));

            // The churn window and its process have gone by here, which is the
            // condition the walk below has to survive.
            check("after churn", MessageWindows.Walk(SingletonClass));
        }

        // Every failure is collected rather than thrown at, so the message names
        // which walk lost the window and what the walk reported about itself.
        // A bare "expected true" here would say nothing about whether the walk
        // truncated, restarted, or simply came back short.
        await Assert.That(string.Join(Environment.NewLine, lost)).IsEmpty();
        await Assert.That(walks).IsGreaterThan(2);

        // `restarts` is recorded rather than asserted on: a restart needs a
        // window to die in the microseconds between two FindWindowExW calls, so
        // requiring one would be a flaky test. What is asserted is the invariant
        // a missing ERROR_INVALID_WINDOW_HANDLE check breaks — a walk that
        // quietly stops early and loses a window that was there the whole time.
        await Assert.That(restarts).IsGreaterThanOrEqualTo(0);

        void check(string phase, MessageWindowWalk walk)
        {
            walks++;
            restarts += walk.Restarts;

            if (walk.Truncated)
            {
                lost.Add($"walk {walks} ({phase}) truncated after {walk.Restarts} restarts");
                return;
            }

            if (!walk.Windows.Any(window => string.Equals(MessageWindows.WindowText(window.Handle), name, StringComparison.Ordinal)))
            {
                lost.Add($"walk {walks} ({phase}) found {walk.Windows.Count} windows after {walk.Restarts} restarts and none was the stable one");
            }
        }
    }

    /// <summary>
    /// Publishes one more window of the same class, walks, and takes it away
    /// again as the scope unwinds.
    /// </summary>
    private static async Task<MessageWindowWalk> WalkBesideOneMoreWindowAsync(string workingDirectory)
    {
        using var churn = new JobObjectScope();

        _ = await PlantedProbe.PublishWindowAsync(
            churn,
            ProbeExecutable,
            workingDirectory,
            SingletonClass,
            $@"C:\BrowserAI-churn-{Guid.NewGuid():N}");

        return MessageWindows.Walk(SingletonClass);
    }

    private static string ProbeExecutable { get; } = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");
}

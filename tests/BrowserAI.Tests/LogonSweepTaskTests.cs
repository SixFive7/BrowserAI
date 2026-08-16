// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Xml.Linq;
using BrowserAI.Runtime;
using BrowserAI.Sessions;

namespace BrowserAI.Tests;

/// <summary>
/// The logon task's definition — the second sweep trigger, built here and
/// registered at [step 19](../../plan/build-order.md#19-velopack-packaging-and-the-update-lane).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two of these settings are silent-success traps, which is why they are
/// asserted rather than reviewed.</b> A task configured <i>"run whether user is
/// logged on or not"</i> lands in session 0, where
/// <c>FindWindowExW(HWND_MESSAGE, …)</c> sees <b>no message windows at all</b> —
/// it would sweep, find nothing and report success forever (race <b>R5</b>). And
/// a task that ran a second instance on top of a running one would defeat
/// try-acquire-and-skip at the scheduler level rather than at the mutex
/// (race <b>R9</b>).
/// </para>
/// <para>
/// <b>Registration is not exercised by the suite, deliberately.</b> A test that
/// registered a scheduled task would leave machine state behind on every run
/// from a checkout, and there is no install to hook until §G lands. That it
/// registers non-elevated was established by hand and recorded in
/// [kb](../../kb/windows/detection.md#the-logon-sweep-task).
/// </para>
/// </remarks>
internal sealed class LogonSweepTaskTests
{
    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Test]
    public async Task TheTaskRunsInTheUsersOwnSessionAndNotAsAService()
    {
        var task = Parse();
        var principal = task.Descendants(Name("Principal")).Single();

        // R5. InteractiveToken is the setting; anything else -- Password, S4U --
        // is "run whether user is logged on or not", which is session 0.
        await Assert.That((string?)principal.Element(Name("LogonType"))).IsEqualTo("InteractiveToken");
        await Assert.That((string?)principal.Element(Name("RunLevel"))).IsEqualTo("LeastPrivilege");

        // ⚠️ A UserId rather than a GroupId, measured rather than chosen: Task
        // Scheduler's schema permits LogonType only beside a UserId, and
        // schtasks refuses the file outright with a group principal. A group
        // would have run in the user's session too -- but only by implication,
        // and the assertion above is the whole point.
        await Assert.That((string?)principal.Element(Name("UserId"))).IsEqualTo(User);
        await Assert.That(principal.Element(Name("GroupId"))).IsNull();

        // Never elevated. A sweeper needs no more rights than the browsers it
        // started, which run as the user.
        await Assert.That(Xml()).DoesNotContain("HighestAvailable");
    }

    [Test]
    public async Task TheTriggerIsLogonWithOneRepetitionRatherThanASleepInsideTheProcess()
    {
        var task = Parse();
        var trigger = task.Descendants(Name("LogonTrigger")).Single();
        var repetition = trigger.Element(Name("Repetition"))!;

        await Assert.That((string?)trigger.Element(Name("Enabled"))).IsEqualTo("true");

        // An immediate pass plus one re-check, because nothing marks the end of
        // Windows' own app restore and neither trigger tries to win that race.
        await Assert.That((string?)repetition.Element(Name("Interval"))).IsEqualTo("PT10M");
        await Assert.That((string?)repetition.Element(Name("Duration"))).IsEqualTo("PT15M");
        await Assert.That((string?)repetition.Element(Name("StopAtDurationEnd"))).IsEqualTo("true");
        await Assert.That(LogonSweepTask.ReCheckInterval).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task TheTaskRunsBrowserAiItselfWithTheOneArgumentTheProductMatches()
    {
        var task = Parse();
        var exec = task.Descendants(Name("Exec")).Single();

        await Assert.That((string?)exec.Element(Name("Command"))).IsEqualTo(Executable);
        await Assert.That((string?)exec.Element(Name("Arguments"))).IsEqualTo(LogonSweepTask.SweepArgument);

        // R9 at the scheduler level: a pass that overruns the ten-minute
        // re-check must not be joined by a second instance.
        await Assert.That((string?)task.Descendants(Name("MultipleInstancesPolicy")).Single()).IsEqualTo("IgnoreNew");

        // A wedged pass would hold Global\BrowserAI-Sweep forever and silently
        // disable every other BrowserAI's sweep on the machine, so there is a
        // ceiling on it.
        await Assert.That((string?)task.Descendants(Name("ExecutionTimeLimit")).Single()).IsEqualTo("PT5M");
    }

    [Test]
    public async Task TheDefinitionIsWellFormedAndCarriesNoLockNameOfItsOwn()
    {
        var xml = Xml();

        await Assert.That(xml).StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
        await Assert.That(Parse().Name).IsEqualTo(Name("Task"));

        // R4 from the task's side: it names no object at all. Whatever mutex the
        // binary uses is the one the task uses, because the task only names the
        // binary.
        await Assert.That(xml).DoesNotContain(LockScopes.Sweep);
        await Assert.That(xml).Contains(LogonSweepTask.TaskPath);
    }

    [Test]
    public async Task AnAuthorAndAPathCarryingXmlMetacharactersAreEscaped()
    {
        // The executable path comes from the install location and the author
        // from packaging metadata; neither is this product's string, and an
        // unescaped ampersand would produce a definition Task Scheduler refuses
        // with an error naming nothing useful.
        var xml = LogonSweepTask.Xml(@"C:\Program Files\A & B\BrowserAI.exe", "Jori <Huisman> & Co", @"DOMAIN\a&b");

        await Assert.That(xml).Contains("A &amp; B");
        await Assert.That(xml).Contains("Jori &lt;Huisman&gt; &amp; Co");
        await Assert.That(xml).Contains(@"DOMAIN\a&amp;b");
        await Assert.That(XDocument.Parse(xml).Root).IsNotNull();
    }

    private static string Executable => @"C:\Program Files\BrowserAI\current\BrowserAI.exe";

    private static string User => "S-1-5-21-0-0-0-1000";

    private static string Xml() => LogonSweepTask.Xml(Executable, "BrowserAI", User);

    private static XElement Parse() => XDocument.Parse(Xml()).Root!;

    private static XName Name(string element) => XName.Get(element, TaskNamespace);
}

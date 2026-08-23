// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// <c>browserai_catch_up</c>: what the session was doing, and what is in its
/// directory now.
/// </summary>
/// <remarks>
/// <para>
/// <b>The test that matters is the disagreement one.</b> The two halves of this
/// answer come from different places and are expected to differ — a log-only
/// answer would say <i>"no credential tools were used"</i> about a directory
/// full of live session cookies, because cookies arrive from navigation rather
/// than from tools. So the arm below plants a cookie store the log knows nothing
/// about and requires the answer to report it anyway.
/// </para>
/// <para>
/// <b>And the read-only claim is asserted rather than described.</b> The tool
/// runs against a session this BrowserAI is driving, and the record is compared
/// byte for byte before and after: a version that appended its own entry, or took
/// the per-directory gate, would fail the one case the tool exists for.
/// </para>
/// </remarks>
internal sealed class CatchUpTests
{
    [Test]
    public async Task ItReportsWhatWasDoneAndWhatIsHereAndTheTwoAreSeparate()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour(),
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "catch-up");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "reproducing the checkout 500 on staging",
        });

        _ = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,ok",
            ["session"] = directory,
            ["why"] = "establishing that the page loads at all",
        });

        // ⚠️ PLANTED BEHIND THE LOG'S BACK, which is the whole point: a browser
        // writes a cookie store from NAVIGATION, so nothing in the log will ever
        // mention it. A log-only answer reports "no credential tools were used"
        // about exactly this directory.
        var store = Path.Combine(directory, SessionLayout.ProfileFolderName, "Default", "Network");
        _ = Directory.CreateDirectory(store);
        await File.WriteAllBytesAsync(Path.Combine(store, "Cookies"), new byte[4096]);

        // And a HAR, which is the file the answer has to call out by name.
        var har = Path.Combine(directory, SessionLayout.OutputFolderName, "network-2026-08-20.har");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(har)!);
        await File.WriteAllTextAsync(har, """{"log":{"entries":[]}}""");

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        }));

        // The two halves are labelled and separate, because a reader has to know
        // which source each fact came from before it can act on a disagreement.
        await Assert.That(text).Contains("WHAT WAS DONE HERE");
        await Assert.That(text).Contains("WHAT IS HERE NOW");

        // The log half: both entries, with what each was for.
        await Assert.That(text).Contains(SessionToolSurface.Init);
        await Assert.That(text).Contains("reproducing the checkout 500 on staging");
        await Assert.That(text).Contains("browser_navigate");
        await Assert.That(text).Contains("establishing that the page loads at all");
        await Assert.That(text).Contains("url=data:text/html,ok");

        // ⚠️ THE DISAGREEMENT. Nothing in the log mentions a cookie, and the
        // answer says the profile holds a cookie store anyway.
        await Assert.That(text).DoesNotContain("browser_cookie");
        await Assert.That(text).Contains("CREDENTIALS");
        await Assert.That(text).Contains("cookies arrive from navigation");

        // The HAR, named, with what it is rather than only that it exists.
        await Assert.That(text).Contains("network-2026-08-20.har");
        await Assert.That(text).Contains("PLAINTEXT CREDENTIALS");
        await Assert.That(text).Contains("in clear text");

        // Age, last touched, size and a breakdown by kind.
        await Assert.That(text).Contains("created:");
        await Assert.That(text).Contains("last touched:");
        await Assert.That(text).Contains("total:");
        await Assert.That(text).Contains("profile:");
        await Assert.That(text).Contains($"{SessionLayout.OutputFolderName} (unfiled):");

        // And whether anything is driving it right now, which is the fact that
        // decides whether the caller may act on any of the above.
        await Assert.That(text).Contains("in use: YES");
    }

    /// <summary>
    /// It changes nothing — not the record, not the log — and works on a session
    /// something else is holding.
    /// </summary>
    /// <remarks>
    /// <b>Byte-for-byte, because "the log did not grow" is a weaker claim.</b> An
    /// implementation that took the per-directory gate and rewrote the record
    /// with identical content would pass a count check, still refuse a session a
    /// live peer holds, and still move the record's mtime.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ItIsReadOnlyAndAnswersForASessionSomethingElseIsDriving()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "read-only");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session that is being held while it is read",
        });

        // Read the way a bystander has to: the holder keeps the file open
        // `FileShare.Read`, so `File.ReadAllBytes` -- which asks for a share
        // mode of None -- is refused.
        var file = Path.Combine(directory, SessionLayout.LockFileName);
        var before = ReadSharing(file);
        var written = File.GetLastWriteTimeUtc(file);

        // This BrowserAI is driving the session, so the per-directory gate is
        // exactly what a writing implementation would have to contend for.
        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        }));

        await Assert.That(text).Contains("a session that is being held while it is read");
        await Assert.That(ReadSharing(file)).IsEquivalentTo(before);
        await Assert.That(File.GetLastWriteTimeUtc(file)).IsEqualTo(written);

        // The record's own log did not gain an entry for the read.
        var record = SessionLock.ReadRecord(SessionPath.Resolve(directory))!;

        await Assert.That(record.Log.Count).IsEqualTo(1);
        await Assert.That(record.Log.Any(entry => entry.Tool == SessionToolSurface.CatchUp)).IsFalse();
    }

    /// <summary>
    /// <c>browserai_init</c>'s purpose is printed <b>once</b>, in full, under
    /// <c>why:</c> — and never a second time under <c>with:</c> as a stump.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The defect this holds against is not an aesthetic one.</b>
    /// <c>browserai_init</c> is the one call whose <c>why</c> and whose
    /// <c>purpose</c> are the same string — it takes no separate <c>why</c>, so
    /// the purpose <i>is</i> the why. The entry printed it in full under
    /// <c>why:</c> and then again directly beneath under <c>with: purpose=…</c>,
    /// cut at <see cref="LockRecord.ArgumentValueMaximumLength"/> characters with
    /// <c>(+N more characters)</c> after it. <b>Two adjacent lines, the second
    /// one shorter and different</b>: nothing in the answer says the second is a
    /// truncation of the first rather than a value that disagrees with it, and a
    /// model reading its own session back has to guess which one is current.
    /// </para>
    /// <para>
    /// <b>The purpose is deliberately longer than the argument cap and shorter
    /// than <see cref="LockRecord.WhyMaximumLength"/></b>, and both are asserted
    /// rather than eyeballed — a purpose under 200 characters would make this
    /// test pass against the defect it exists for.
    /// </para>
    /// <para>
    /// <b>The <c>with:</c> line itself must survive.</b> <c>init</c> also carries
    /// <c>directory</c>, which is still recorded, so an implementation that
    /// dropped the whole line rather than the one duplicated argument fails here.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInitPurposeIsPrintedInFullOnceAndNeverAgainAsATruncatedArgument()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "printed-once");

        const string Purpose =
            "reproducing the checkout 500 on staging: the cart posts to /checkout, the response is a 500 with an "
            + "empty body, and it only happens for accounts whose default address is outside the billing country, "
            + "which is why the fixture needs a Norwegian address on a UK account";

        // The preconditions that make a failure possible at all. Under the first
        // the argument is never cut and there is no stump to find; over the
        // second the `why:` line is cut too and the two lines stop disagreeing.
        await Assert.That(Purpose.Length).IsGreaterThan(LockRecord.ArgumentValueMaximumLength);
        await Assert.That(Purpose.Length).IsLessThanOrEqualTo(LockRecord.WhyMaximumLength);

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = Purpose,
        });

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        }));

        // Printed, in full, as the entry's own reason.
        await Assert.That(text).Contains($"why: {Purpose}");

        // ⚠️ THE CLAIM. The stump is composed from the product's own cap rather
        // than typed, so it stays the string the defect actually produced.
        var stump = Purpose[..LockRecord.ArgumentValueMaximumLength];
        var dropped = Purpose.Length - LockRecord.ArgumentValueMaximumLength;

        await Assert.That(text).DoesNotContain($"{stump}… (+{dropped} more characters)");
        await Assert.That(text).DoesNotContain("more characters)");
        await Assert.That(text).DoesNotContain($"{SessionToolSurface.PurposeParameter}=");

        // And the line is still there, carrying what init's OTHER argument said.
        await Assert.That(text).Contains($"with: directory={directory}");

        // At the record, not only in the rendering: the entry never gained the
        // duplicate, so nothing downstream can print it either.
        var record = SessionLock.ReadRecord(SessionPath.Resolve(directory))!;

        await Assert.That(record.Log[0].Tool).IsEqualTo(SessionToolSurface.Init);
        await Assert.That(record.Log[0].Why).IsEqualTo(Purpose);
        await Assert.That(record.Log[0].Arguments.Select(argument => argument.Name).ToArray())
            .IsEquivalentTo(["directory"]);
    }

    /// <summary>
    /// <c>browserai_resume</c> and <c>browserai_set_purpose</c> keep printing
    /// <c>purpose</c> beside their own <c>why</c>, because there the two are
    /// different values.
    /// </summary>
    /// <remarks>
    /// <b>This is the other half of the arm above and it is what keeps the
    /// suppression narrow.</b> On these two tools the caller sends a standing
    /// description <i>and</i> a disposable reason, and an entry that dropped
    /// either would lose the fact that the purpose moved at the moment it moved.
    /// A version that suppressed <c>purpose</c> everywhere passes the test above
    /// and fails this one.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ResumeAndSetPurposeStillRecordPurposeBesideTheirOwnWhy()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "two-values");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "reproducing the checkout 500 on staging",
        });

        _ = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "and the same 500 on the mobile checkout",
            ["why"] = "picking this up after the overnight run stopped",
        });

        _ = await CallAsync(rig, SessionToolSurface.SetPurpose, new JsonObject
        {
            ["session"] = directory,
            ["purpose"] = "tracking the checkout redirect loop on staging",
            ["why"] = "the 500 turned out to be a redirect loop",
        });

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        }));

        await Assert.That(text).Contains("why: picking this up after the overnight run stopped");
        await Assert.That(text).Contains($"{SessionToolSurface.PurposeParameter}=and the same 500 on the mobile checkout");

        await Assert.That(text).Contains("why: the 500 turned out to be a redirect loop");
        await Assert.That(text).Contains($"{SessionToolSurface.PurposeParameter}=tracking the checkout redirect loop on staging");

        var record = SessionLock.ReadRecord(SessionPath.Resolve(directory))!;

        await Assert.That(record.Log.Select(entry => entry.Tool).ToArray())
            .IsEquivalentTo([SessionToolSurface.Init, SessionToolSurface.Resume, SessionToolSurface.SetPurpose]);

        foreach (var entry in record.Log.Where(entry => entry.Tool != SessionToolSurface.Init))
        {
            await Assert.That(entry.Arguments.Any(argument => argument.Name == SessionToolSurface.PurposeParameter)).IsTrue();
        }
    }

    [Test]
    public async Task ADirectoryThatIsNotASessionIsRefusedWithSomewhereToGo()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "never-a-session");
        _ = Directory.CreateDirectory(directory);

        var answer = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();

        var text = TextOf(answer);

        await Assert.That(text).Contains(SessionLayout.LockFileName);
        await Assert.That(text).Contains(SessionToolSurface.List);
        await Assert.That(text).Contains(SessionToolSurface.Init);
    }

    /// <summary>Reads a file its holder has open.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its bytes.</returns>
    private static byte[] ReadSharing(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();

        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}

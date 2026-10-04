// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// <c>browserai_catch_up</c>: what the session was doing, and what is in its
/// directory now.
/// </summary>
/// <remarks>
/// <para>
/// <b>The test that matters is the disagreement one.</b> The two halves of this
/// answer come from different places and are expected to differ -- a log-only
/// answer would say <i>"no credential tools were used"</i> about a directory
/// full of live session cookies, because cookies arrive from navigation and
/// not from tools. So the arm below plants a cookie store the log knows nothing
/// about and requires the answer to report it anyway.
/// </para>
/// <para>
/// <b>And the read-only claim is asserted, not described.</b> The tool
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
            ["why"] = "the suite reading back what this session did",
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

        // ⚠️ THE DISAGREEMENT. Nothing in the log mentions a cookie, and the
        // answer says the profile holds a cookie store anyway.
        await Assert.That(text).DoesNotContain("browser_cookie");
        await Assert.That(text).Contains("CREDENTIALS");
        await Assert.That(text).Contains("cookies arrive from navigation");

        // The HAR, named, with what it is and not only that it exists.
        await Assert.That(text).Contains("network-2026-08-20.har");
        await Assert.That(text).Contains("PLAINTEXT CREDENTIALS");
        await Assert.That(text).Contains("in clear text");

        // Age, last touched, size and a breakdown by kind.
        await Assert.That(text).Contains("created:");
        await Assert.That(text).Contains("last touched:");
        await Assert.That(text).Contains("total:");
        await Assert.That(text).Contains("profile:");
        await Assert.That(text).Contains($"{SessionLayout.OutputFolderName} (unfiled):");

        // ⚠️ Q100e. The output size, and the sentence that says whose decision
        // retention is: nothing here is ever deleted on a schedule or at a size,
        // so the number is the whole of what a caller has to act on.
        await Assert.That(text).Contains("output:");
        await Assert.That(text).Contains("BrowserAI never deletes any of it");
        await Assert.That(text).Contains(SessionToolSurface.Destroy);

        // Every page says which one it is, how many there are, and where in the
        // whole log its entries sit.
        await Assert.That(text).Contains("page 1 of 1");

        // And whether anything is driving it right now, which is the fact that
        // decides whether the caller may act on any of the above.
        await Assert.That(text).Contains("in use: YES");
    }

    /// <summary>
    /// Every file that holds login data in clear text is named: the saved login
    /// <c>browser_storage_state</c> writes, a Playwright trace, and a transcript --
    /// and a file that only looks like one of them is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q365.4 a, decided 2026-10-03 by the maintainer, in his words: <i>"Now
    /// about Q365.4. Let's mention all files."</i></b> Until then the answer named
    /// the profile's cookie store and any HTTP Archive, and nothing else -- while a
    /// saved login holds the cookie values, a trace's network log holds every
    /// header, and a transcript holds every argument a call was given, so a typed
    /// password. The three lines are his approved wording, asserted whole.
    /// </para>
    /// <para>
    /// <b>The saved login is recognised by what it holds and not by its
    /// name</b>, because <c>browser_storage_state</c> writes whatever
    /// <c>filename</c> it is given: Playwright's storage state is an object with a
    /// <c>cookies</c> array and an <c>origins</c> array. The controls are a JSON
    /// file of another shape and a <c>session.md</c> outside a
    /// <c>session-&lt;milliseconds&gt;</c> folder.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryFileThatHoldsLoginDataInClearTextIsNamed()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "all-the-files");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "holds every kind of file with login data in it",
        });

        var output = Path.Combine(directory, SessionLayout.OutputFolderName);

        // A saved login under a name the caller chose, and a JSON of another shape.
        var saved = Path.Combine(output, "login-state.json");
        await File.WriteAllTextAsync(saved, """{"cookies":[{"name":"sid","value":"secret","domain":"127.0.0.1","path":"/","expires":-1,"httpOnly":true,"secure":false,"sameSite":"Lax"}],"origins":[]}""");
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), """{"cookies":3,"rows":[1,2,3]}""");

        // A trace, as browser_start_tracing lays one out.
        var traces = Path.Combine(output, "traces");
        _ = Directory.CreateDirectory(Path.Combine(traces, "resources"));
        await File.WriteAllTextAsync(Path.Combine(traces, "trace-1791000000000.trace"), "{}");
        await File.WriteAllTextAsync(Path.Combine(traces, "trace-1791000000000.network"), "{}");
        await File.WriteAllTextAsync(Path.Combine(traces, "resources", "0a1b2c"), "body");

        // A transcript, and a session.md somewhere a transcript never is.
        var transcript = Path.Combine(output, "session-1791000000000", "session.md");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(transcript)!);
        await File.WriteAllTextAsync(transcript, "### Tool call: browser_type\n- Args\n");
        _ = Directory.CreateDirectory(Path.Combine(output, "notes"));
        await File.WriteAllTextAsync(Path.Combine(output, "notes", "session.md"), "notes");

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
        }));

        static long sizeOf(string path) => new FileInfo(path).Length;

        var traceBytes = Directory.EnumerateFiles(traces, "*", SearchOption.AllDirectories).Sum(sizeOf);

        await Assert.That(text).Contains(
            $"  ⚠️ PLAINTEXT CREDENTIALS: '{Path.Combine(SessionLayout.OutputFolderName, "login-state.json")}' is a saved login written by browser_storage_state ({Sizes.Describe(sizeOf(saved))}). "
            + "It holds the cookies and site storage needed to sign in as this session, in clear text. Treat the file as a secret and delete it when you are done.\n");

        // ⚠️ The trace's line names its action log since 2026-10-04, 4 a
        // (previously "Its network log holds every request and response with their
        // headers, so session cookies and tokens are in it in clear text."): the
        // action log held the typed password in both families' runs that day.
        await Assert.That(text).Contains(
            $"  ⚠️ PLAINTEXT CREDENTIALS: '{Path.Combine(SessionLayout.OutputFolderName, "traces")}' is a Playwright trace ({Sizes.Describe(traceBytes)}). "
            + TraceHolds + " Treat it as a secret and delete it when you are done.\n");

        await Assert.That(text).Contains(
            $"  ⚠️ PLAINTEXT CREDENTIALS: '{Path.Combine(SessionLayout.OutputFolderName, "session-1791000000000", "session.md")}' is a transcript ({Sizes.Describe(sizeOf(transcript))}). "
            + "It holds every call's arguments, so text typed into the page, passwords included, is in it in clear text. Treat it as a secret and delete it when you are done.\n");

        // ⚠️ THE CONTROLS: a JSON of another shape is not a saved login and a
        // session.md outside a transcript folder is not a transcript, and the trace
        // is named once and not once per file in it. *Since 2026-10-04 both
        // controls are named (previously "are not named"), as files saved under a
        // name a call chose, which is what 4 a asks: every file that can hold
        // something sensitive.*
        await Assert.That(text).DoesNotContain("report.json' is a saved login");
        await Assert.That(text).DoesNotContain($"'{Path.Combine(SessionLayout.OutputFolderName, "notes", "session.md")}' is a transcript");
        await Assert.That(text).Contains($"'{Path.Combine(SessionLayout.OutputFolderName, "notes", "session.md")}' and '{Path.Combine(SessionLayout.OutputFolderName, "report.json")}' are files saved under names a call or a page chose");
        await Assert.That(text.Split("is a Playwright trace").Length - 1).IsEqualTo(1);
    }

    /// <summary>
    /// Every file in a session that can hold something sensitive is named, one line
    /// per kind, with what that kind holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>4 a, decided 2026-10-04 by the maintainer, in his words: <i>"1 a / 2 b /
    /// 3 a / 4 a - is there not also sessions.md or other logs? Name everythign
    /// sensitive."</i></b> One file of every kind BrowserAI and
    /// <c>@playwright/mcp</c> 0.0.83 write into a session, laid out as they lay them
    /// out and named as they name them, measured the same day against headless
    /// Chromium and Firefox: the profile with its cookie store, an HTTP Archive, a
    /// saved login, a trace, a transcript, a request the network tools saved, a
    /// console log, two page snapshots, twelve screenshots, a PDF, a video, two
    /// files saved by name, a download, and BrowserAI's own record.
    /// </para>
    /// <para>
    /// <b>Twelve screenshots, because ten are named by path</b> and the rest by
    /// count, so a session holding hundreds does not make the answer longer than a
    /// client hands a model whole. The lock file is the control: it holds a process
    /// id and nothing a page wrote, and it is not named.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryFileThatCanHoldSomethingSensitiveIsNamedKindByKind()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "every-kind");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "holds every kind of file that can hold something sensitive",
        });

        var output = Path.Combine(directory, SessionLayout.OutputFolderName);

        async Task<string> write(string relative, string content)
        {
            var path = Path.Combine(directory, relative);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
            return relative;
        }

        // The profile, with Chromium's cookie store and history where Chromium keeps them.
        var cookies = await write(Path.Combine(SessionLayout.ProfileFolderName, "Default", "Network", "Cookies"), "cookies");
        _ = await write(Path.Combine(SessionLayout.ProfileFolderName, "Default", "History"), "history");

        var har = await write(Path.Combine(SessionLayout.OutputFolderName, "network-20261004-160219349.har"), "{\"log\":{}}");
        var saved = await write(Path.Combine(SessionLayout.OutputFolderName, "storage-state-2026-10-04T14-02-25-098Z.json"), "{\"cookies\":[],\"origins\":[]}");
        _ = await write(Path.Combine(SessionLayout.OutputFolderName, "traces", "trace-1791122541386.trace"), "{}");
        _ = await write(Path.Combine(SessionLayout.OutputFolderName, "traces", "trace-1791122541386.network"), "{}");
        _ = await write(Path.Combine(SessionLayout.OutputFolderName, "traces", "screencast", "page@0541cd-1791122541403.jpeg"), "jpeg");
        var transcript = await write(Path.Combine(SessionLayout.OutputFolderName, "session-1791122540247", "session.md"), "### Tool call: browser_type\n");
        var request = await write(Path.Combine(SessionLayout.OutputFolderName, "request-2026-10-04T14-02-24-001Z.txt"), "authorization: Bearer x");
        var console = await write(Path.Combine(SessionLayout.OutputFolderName, "console-2026-10-04T14-02-20-258Z.log"), "[     12ms] [LOG] x");
        var snapshot = await write(Path.Combine(SessionLayout.OutputFolderName, "page-2026-10-04T14-02-20-356Z.yml"), "- textbox \"User\" [ref=e5]: x");
        var named = await write(Path.Combine(SessionLayout.OutputFolderName, "snapshot-after-typing.yml"), "- textbox \"User\" [ref=e5]: x");
        var images = new List<string>();

        for (var index = 0; index < 12; index++)
        {
            images.Add(await write(Path.Combine(SessionLayout.OutputFolderName, $"page-2026-10-04T14-02-{index + 10}-215Z.png"), "png"));
        }

        var pdf = await write(Path.Combine(SessionLayout.OutputFolderName, "page-2026-10-04T14-02-22-372Z.pdf"), "%PDF");
        var video = await write(Path.Combine(SessionLayout.OutputFolderName, "video-2026-10-04T14-02-21-399Z.webm"), "webm");
        var download = await write(Path.Combine(SessionLayout.OutputFolderName, "q371-sample.txt"), "downloaded");
        var evaluation = await write(Path.Combine(SessionLayout.OutputFolderName, "evaluation.json"), "{\"ls\":1}");
        var inFlight = await write(Path.Combine(SessionLayout.DownloadsFolderName, "0f1e2d3c-4b5a"), "downloading");

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back every kind of file",
            ["session"] = directory,
        }));

        long sizeOf(string relative) => new FileInfo(Path.Combine(directory, relative)).Length;
        long folder(string relative) => Directory.EnumerateFiles(Path.Combine(directory, relative), "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);

        var missing = new List<string>();

        void expect(string line)
        {
            if (!text.Contains("  " + line + "\n", StringComparison.Ordinal))
            {
                missing.Add(line);
            }
        }

        expect($"⚠️ CREDENTIALS: '{SessionLayout.ProfileFolderName}' is the browser profile ({Sizes.Describe(folder(SessionLayout.ProfileFolderName))}). It holds the cookie store at '{cookies}', "
            + "so this session may be signed in to something, whether or not any cookie tool appears above -- cookies arrive from navigation -- "
            + $"and the sites' stored data, the history of the pages visited, the cache of what they served, and the tabs that were open with what was typed into their fields, passwords left out. {SessionToolSurface.Destroy} is what removes it.");
        expect($"⚠️ PLAINTEXT CREDENTIALS: '{har}' is an HTTP Archive ({Sizes.Describe(sizeOf(har))}). A HAR records every request and response including headers, so every bearer token and session cookie that crossed the wire is in it in clear text. Treat the file as a secret and delete it when you are done.");
        expect($"⚠️ PLAINTEXT CREDENTIALS: '{saved}' is a saved login written by browser_storage_state ({Sizes.Describe(sizeOf(saved))}). It holds the cookies and site storage needed to sign in as this session, in clear text. Treat the file as a secret and delete it when you are done.");
        expect($"⚠️ PLAINTEXT CREDENTIALS: '{Path.Combine(SessionLayout.OutputFolderName, "traces")}' is a Playwright trace ({Sizes.Describe(folder(Path.Combine(SessionLayout.OutputFolderName, "traces")))}). {TraceHolds} Treat it as a secret and delete it when you are done.");
        expect($"⚠️ PLAINTEXT CREDENTIALS: '{transcript}' is a transcript ({Sizes.Describe(sizeOf(transcript))}). It holds every call's arguments, so text typed into the page, passwords included, is in it in clear text. Treat it as a secret and delete it when you are done.");
        expect($"⚠️ PLAINTEXT CREDENTIALS: '{request}' is a request or a response saved by browser_network_request ({Sizes.Describe(sizeOf(request))}). It holds headers or a body as they crossed the wire, so cookies, tokens and what the server sent back are in it in clear text. Treat the file as a secret and delete it when you are done.");
        expect($"⚠️ PLAINTEXT CREDENTIALS: '{snapshot}' and '{named}' are page snapshots ({Sizes.Describe(sizeOf(snapshot) + sizeOf(named))}). They hold the text of the pages and what was typed into their fields, passwords included, in clear text. Treat the files as secrets and delete them when you are done.");
        expect($"⚠️ SENSITIVE: '{console}' is a log a browser tool wrote ({Sizes.Describe(sizeOf(console))}). It holds what the pages wrote to their console, or the addresses they requested, with any token in them, in clear text. Treat the file as a secret and delete it when you are done.");
        expect($"⚠️ SENSITIVE: {string.Join(", ", images.Take(10).Select(image => $"'{image}'"))} and 2 more are images: screenshots, or pictures a page served ({Sizes.Describe(images.Sum(sizeOf))}). They show whatever was on the pages. Treat the files as secrets and delete them when you are done.");
        expect($"⚠️ SENSITIVE: '{pdf}' is a PDF: a page saved as one, or one a page served ({Sizes.Describe(sizeOf(pdf))}). It holds the page as printed. Treat the file as a secret and delete it when you are done.");
        expect($"⚠️ SENSITIVE: '{video}' is a video the browser recorded ({Sizes.Describe(sizeOf(video))}). It shows everything the pages showed while it recorded. Treat the file as a secret and delete it when you are done.");
        expect($"⚠️ SENSITIVE: '{evaluation}' and '{download}' are files saved under names a call or a page chose: downloads, or tools' answers saved with 'filename' ({Sizes.Describe(sizeOf(evaluation) + sizeOf(download))}). They hold whatever the pages served or the tools returned, and a saved request holds its headers, cookies and tokens included, in clear text. Treat the files as secrets and delete them when you are done.");
        expect($"⚠️ SENSITIVE: '{inFlight}' is a file the browser downloaded ({Sizes.Describe(sizeOf(inFlight))}). It holds whatever the page served. Treat the file as a secret and delete it when you are done.");

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();

        // BrowserAI's own record, one thing however many files SQLite keeps it in,
        // named by what it holds; and the lock beside it, which holds a process id,
        // is not named. Its size is not asserted: the read writes the call's own row
        // after the walk, so the record is larger once the answer is back.
        var record = text.Split('\n').SingleOrDefault(line => line.StartsWith($"  ⚠️ SENSITIVE: '{SessionLayout.DataFileName}' is this session's record (", StringComparison.Ordinal));

        await Assert.That(record).IsNotNull();
        await Assert.That(record!).EndsWith(
            $"). It holds every call's 'why', the session's purposes and the text of every failure, which can quote a page, in clear text. {SessionToolSurface.Destroy} is what removes it.");
        await Assert.That(text).DoesNotContain($"'{SessionLayout.LockFileName}'");
    }

    /// <summary>
    /// What the trace's line says it holds, since 4 a named its action log.
    /// </summary>
    private const string TraceHolds =
        "Its action log holds every action with the text it typed, passwords included, and what the pages showed and logged; "
        + "its network log holds every request with its headers, cookies included; and its saved resources hold what the pages served. "
        + "So typed text and session cookies and tokens are in it in clear text.";

    /// <summary>
    /// It changes nothing -- not the record, not the log -- and works on a session
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
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
        }));

        await Assert.That(text).Contains("a session that is being held while it is read");
        await Assert.That(ReadSharing(file)).IsEquivalentTo(before);
        await Assert.That(File.GetLastWriteTimeUtc(file)).IsEqualTo(written);

        // ⚠️ AND THE LOG GAINS THE READ'S OWN ROW SINCE 2026-10-04, written after
        // the answer was read so the answer does not report itself -- the
        // maintainer's words of 2026-10-03: "browserai_catch_up should take a
        // why." Previously the log gained nothing, and this arm asserted that.
        // The guard file above is still untouched: the row goes into the record
        // this BrowserAI already holds open.
        var log = RecordedSession.LogOf(directory);

        await Assert.That(log.Count).IsEqualTo(2);
        await Assert.That(text).DoesNotContain("the suite reading back what this session did");
        await Assert.That(log[^1].Tool).IsEqualTo(SessionToolSurface.CatchUp);
        await Assert.That(log[^1].Why).IsEqualTo("the suite reading back what this session did");
        await Assert.That(log[^1].Outcome).IsEqualTo(SessionStore.Successful);
    }

    /// <summary>
    /// <c>browserai_catch_up</c> refuses a call with no <c>why</c>, and a session
    /// this BrowserAI does not hold is read without a row being written to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Decided 2026-10-03 by the maintainer, in his words:</b> <i>"browserai_catch_up
    /// should take a why. All other tool calls are fine like they are now when it
    /// comes to the why argument."</i> It is required, as on every other call that
    /// names a session, and recorded on the session the way every call is.
    /// </para>
    /// <para>
    /// <b>Only the holder writes a session's record</b>, so a session nobody in
    /// this process holds is read and not written: the <c>why</c> goes to this
    /// BrowserAI's own log instead. A decision of 2026-10-04 for the maintainer's
    /// review.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ItTakesARequiredWhyAndWritesNoRowToASessionItDoesNotHold()
    {
        var root = Path.Combine(ScratchRoot.Path, $"catch-up-why-{Guid.NewGuid():N}");
        var directory = Path.Combine(root, "held-elsewhere-then-released");

        // Made by one BrowserAI, which then goes away and releases it.
        await using (var first = RigSessionEnvironment.Create(opensDefaultSession: false))
        await using (var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: first))
        {
            _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = directory,
                ["purpose"] = "a session read by a BrowserAI that does not hold it",
            });
        }

        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var reader = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var refused = await CallAsync(reader, SessionToolSurface.CatchUp, new JsonObject { ["session"] = directory });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).IsEqualTo(SessionErrors.WhyMissing(SessionToolSurface.CatchUp));

        var before = RecordedSession.LogOf(directory).Count;

        var read = await CallAsync(reader, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite reading a session this BrowserAI does not hold",
        });

        await Assert.That((bool?)read["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(read)).Contains("a session read by a BrowserAI that does not hold it");
        await Assert.That(RecordedSession.LogOf(directory).Count).IsEqualTo(before);
    }

    /// <summary>
    /// <c>browserai_init</c>'s purpose is printed <b>once</b>, in full, and
    /// never a second time as a stump.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The defect this holds against was not an aesthetic one.</b>
    /// <c>browserai_init</c> is the one call whose <c>why</c> and whose
    /// <c>purpose</c> are the same string -- it takes no separate <c>why</c>, so
    /// the purpose <i>is</i> the why. The entry printed it in full under
    /// <c>why:</c> and then again directly beneath under <c>with: purpose=...</c>,
    /// cut at 200 characters with <c>(+N more characters)</c> after it.
    /// <b>Two adjacent lines, the second one shorter and different</b>: nothing
    /// in the answer said the second was a truncation of the first and not a
    /// value that disagreed with it.
    /// </para>
    /// <para>
    /// ⚠️ <b>The mechanism that produced it is gone (2026-08-26): log rows carry
    /// no arguments at all.</b> So this is kept as a regression and not as
    /// the fix's own test -- what it now holds is that no cut marker of any kind
    /// reaches this answer, which is the property a caller relies on when it
    /// reads a <c>why</c> back and acts on it.
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

        // The precondition that made a failure possible at all: under the old
        // 200-character argument cap this string was cut, and a shorter one
        // would make this test pass against the defect it exists for.
        await Assert.That(Purpose.Length).IsGreaterThan(200);

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = Purpose,
        });

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
        }));

        // Printed, in full, as the row's own reason.
        await Assert.That(text).Contains($"why: {Purpose}");

        // ⚠️ THE CLAIM. Nothing anywhere in the answer is a cut.
        await Assert.That(text).DoesNotContain("more characters)");
        await Assert.That(text).DoesNotContain($"{SessionToolSurface.PurposeParameter}=");

        // At the record, not only in the rendering: the row carries the whole
        // string, so nothing downstream can print a stump of it either.
        var log = RecordedSession.LogOf(directory);

        await Assert.That(log[0].Tool).IsEqualTo(SessionToolSurface.Init);
        await Assert.That(log[0].Why).IsEqualTo(Purpose);
    }

    /// <summary>
    /// A purpose set by <c>browserai_resume</c> or <c>browserai_change_purpose</c>
    /// is still recoverable, and still dated, from the record alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-26 (previously
    /// <c>ResumeAndSetPurposeStillRecordPurposeBesideTheirOwnWhy</c>, asserting
    /// a <c>with: purpose=...</c> line on the log entry).</b> Log rows carry no
    /// arguments, so the new purpose is no longer <i>in</i> the entry -- and that
    /// is the one thing the argument drop genuinely cost, named as a cost and
    /// not glossed. What replaces it is the <c>purpose</c> statement history:
    /// every value the session has been for, each with the instant it was
    /// recorded, so its <b>position in the stream</b> survives as a timestamp
    /// even though it is no longer a line beside the <c>why</c>.
    /// </para>
    /// <para>
    /// <b>Both tools, because the standing description and the disposable reason
    /// say different things</b> -- one lasts, one explains a moment -- and an
    /// implementation that recorded only one of them would lose the fact that
    /// the purpose moved.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ResumeAndSetPurposeRecordTheNewPurposeAsADatedStatement()
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

        _ = await CallAsync(rig, SessionToolSurface.ChangePurpose, new JsonObject
        {
            ["session"] = directory,
            ["purpose"] = "tracking the checkout redirect loop on staging",
            ["why"] = "the 500 turned out to be a redirect loop",
        });

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
        }));

        // The disposable reasons, one per row.
        await Assert.That(text).Contains("why: picking this up after the overnight run stopped");
        await Assert.That(text).Contains("why: the 500 turned out to be a redirect loop");

        // And the standing descriptions, in the block that prints every field
        // that has been more than one thing.
        await Assert.That(text).Contains("how this session got here");
        await Assert.That(text).Contains("and the same 500 on the mobile checkout");
        await Assert.That(text).Contains("tracking the checkout redirect loop on staging");

        var record = SessionLock.ReadRecord(SessionPath.For(directory))!;

        await Assert.That(record.PurposeHistory.Select(statement => statement.Value).ToArray())
            .IsEquivalentTo([
                "reproducing the checkout 500 on staging",
                "and the same 500 on the mobile checkout",
                "tracking the checkout redirect loop on staging",
            ]);

        // Dated, and in order, which is what carries "its position in the
        // stream" now that it is not a line in the stream.
        foreach (var (earlier, later) in record.PurposeHistory.Zip(record.PurposeHistory.Skip(1)))
        {
            await Assert.That(earlier.At).IsLessThanOrEqualTo(later.At);
        }

        // ⚠️ And the catch_up above, since 2026-10-04: it takes a why and writes
        // its own row after it has read.
        await Assert.That(RecordedSession.LogOf(directory).Select(entry => entry.Tool).ToArray())
            .IsEquivalentTo([SessionToolSurface.Init, SessionToolSurface.Resume, SessionToolSurface.ChangePurpose, SessionToolSurface.CatchUp]);
    }

    /// <summary>
    /// The log is paged, numbered from the OLDEST entry, and a page a caller has
    /// already read never changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Oldest-first is what makes a page stable, and it is the whole reason
    /// the numbering runs that way.</b> The log only ever grows at the newest
    /// end and nothing evicts, so under this numbering an append can change the
    /// last page and no other. Numbering from the newest end -- the shape the old
    /// truncation used, which cut from the front -- shifts every boundary on
    /// every call, so page 2 of a live session would be a different set of
    /// entries each time it was fetched.
    /// </para>
    /// <para>
    /// <b>The stability is asserted by appending between two fetches</b>, which
    /// is the only way to tell a stable numbering from one that happens to have
    /// been quiet.
    /// </para>
    /// <para>
    /// <b>And the volatile half is on page 1 alone.</b> The inventory is a fresh
    /// directory walk and the in-use line is a fresh probe, so repeating them
    /// would let two pages of one answer disagree about one session in one
    /// minute -- about information that has nothing to do with the page being
    /// fetched.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLogIsPagedFromTheOldestEndAndAnEarlierPageNeverMoves()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour(),
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "paged");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session with more entries than fit on one page",
        });

        // One past the page size, so there are exactly two pages and the second
        // holds a known number of entries.
        const int Calls = 120;

        for (var i = 0; i < Calls; i++)
        {
            _ = await CallAsync(rig, "browser_navigate", new JsonObject
            {
                ["url"] = "data:text/html,ok",
                ["session"] = directory,
                ["why"] = $"call number {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            });
        }

        var first = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject { ["why"] = "the suite reading back what this session did", ["session"] = directory }));

        // Page 1 by default, and it says which page it is, how many there are,
        // and the call that fetches the next.
        await Assert.That(first).Contains("page 1 of 2");
        await Assert.That(first).Contains("entries 1-100 of 121");
        await Assert.That(first).Contains($"{SessionToolSurface.CatchUp}(session='{directory}', {SessionToolSurface.PageParameter}=2)");

        // Numbered from the oldest, so entry 1 is the init.
        await Assert.That(first).Contains($"1. ");
        await Assert.That(first).Contains(SessionToolSurface.Init);
        await Assert.That(first).Contains("call number 0");

        // The volatile half is here and nowhere else.
        await Assert.That(first).Contains("WHAT IS HERE NOW");
        await Assert.That(first).Contains("in use:");
        await Assert.That(first).Contains("created:");

        var second = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
            [SessionToolSurface.PageParameter] = 2,
        }));

        // ⚠️ 122 and not 121 since 2026-10-04: each catch_up writes its own row
        // AFTER it has read, so the first read above is the 122nd entry by now.
        await Assert.That(second).Contains("page 2 of 2");
        await Assert.That(second).Contains("entries 101-122 of 122");
        await Assert.That(second).Contains("this is the last page");
        await Assert.That(second).Contains($"call number {(Calls - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        // ⚠️ AND NOT THE VOLATILE HALF. Two pages of one answer must not be able
        // to disagree about one session in one minute.
        await Assert.That(second).DoesNotContain("WHAT IS HERE NOW");
        await Assert.That(second).DoesNotContain("in use:");
        await Assert.That(second).DoesNotContain("created:");

        // ⚠️ THE STABILITY CLAIM, and it needs an append between the fetches.
        _ = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,ok",
            ["session"] = directory,
            ["why"] = "a call that lands after page 1 was read",
        });

        var again = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject { ["why"] = "the suite reading back what this session did", ["session"] = directory }));

        // The one thing on page 1 that MAY move is the total, because there is a
        // new entry; the entries themselves are the same set in the same order.
        await Assert.That(Entries(again)).IsEquivalentTo(Entries(first));
        await Assert.That(again).Contains("entries 1-100 of 124");
        await Assert.That(again).DoesNotContain("a call that lands after page 1 was read");

        // Out of range is a refusal that names the range and says which end the
        // numbering starts from.
        var refused = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
            [SessionToolSurface.PageParameter] = 9,
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains("outside this session's log");
        await Assert.That(TextOf(refused)).Contains("numbered from the OLDEST end");
    }

    /// <summary>
    /// A page number outside <c>int</c> is refused, quoting the number the
    /// caller sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It was narrowed with an unchecked cast until 2026-08-26, so an
    /// out-of-range page was served as a DIFFERENT page.</b> Measured that day
    /// through the published binary: <c>page=2</c> on a six-entry session was
    /// correctly refused, and <c>page=4294967297</c> came back
    /// <c>isError=false</c> reading <i>"page 1 of 1, entries 1-6 of 6"</i> --
    /// 2^32 + 1 truncates to 1, and the range check then passed. A caller was
    /// told it was reading the page it asked for.
    /// </para>
    /// <para>
    /// <b>The mirror case is the one that makes this a refusal and not a
    /// clamp:</b> <c>2147483648</c> wraps to <c>-2147483648</c>, so the refusal
    /// quoted a number the caller never sent. Both arms are here because a fix
    /// that only widened the comparison would still misquote.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APageNumberOutsideIntIsRefusedQuotingTheNumberTheCallerSent()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "paged-past-int");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session asked for a page number no page could ever have",
        });

        // 2^32 + 1, which truncates to 1 -- the page that exists.
        var wrapped = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
            [SessionToolSurface.PageParameter] = 4294967297L,
        });

        await Assert.That((bool?)wrapped["isError"]).IsTrue();
        await Assert.That(TextOf(wrapped)).Contains("4294967297");
        await Assert.That(TextOf(wrapped)).Contains("outside this session's log");

        // int.MaxValue + 1, which wraps NEGATIVE. The refusal has to quote what
        // arrived and not what the cast made of it.
        var negative = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
            [SessionToolSurface.PageParameter] = 2147483648L,
        });

        await Assert.That((bool?)negative["isError"]).IsTrue();
        await Assert.That(TextOf(negative)).Contains("2147483648");
        await Assert.That(TextOf(negative)).DoesNotContain("-2147483648");

        // ⚠️ THE POSITIVE CONTROL. Page 1 of this session is still served, so a
        // paging path that had started refusing everything would satisfy both
        // arms above.
        var page1 = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
            [SessionToolSurface.PageParameter] = 1,
        });

        await Assert.That((bool?)page1["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(page1)).Contains("page 1 of 1");
    }

    /// <summary>
    /// A row nothing settled renders as <i>no answer was recorded</i>, and a row
    /// that failed carries what failed.
    /// </summary>
    /// <remarks>
    /// <b>A stale <c>in-flight</c> row is the whole of what a hung call, a dead
    /// child or a killed process leaves</b> -- the row is written before the call
    /// is forwarded, precisely so that those three leave something. What a
    /// reader must never be given is a <c>false</c> there, which is a lie, or a
    /// <c>true</c>, which is worse.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStaleInFlightRowSaysNoAnswerWasRecordedAndAFailureCarriesWhy()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "stale-in-flight");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session with a call that never came back",
        });

        // ⚠️ Planted directly, because the state this renders is one no
        // cooperating process produces: it is what is left when a call was
        // forwarded and the process that forwarded it never returned. Writing it
        // through the store is the same thing that would be on disk afterwards.
        var path = SessionPath.For(directory);

        using (var store = SessionStore.OpenForWriting(path.DataFile))
        {
            _ = store.AppendLog(
                SessionRecordReader.Stamp(DateTimeOffset.Now),
                "browser_navigate",
                "loading the page the process died on",
                SessionStore.InFlight);

            var failed = store.AppendLog(
                SessionRecordReader.Stamp(DateTimeOffset.Now),
                "browser_click",
                "clicking something that was not there",
                SessionStore.InFlight);

            _ = store.Settle(
                failed,
                SessionStore.Failed,
                SessionRecordReader.Stamp(DateTimeOffset.Now),
                System.Text.Encoding.UTF8.GetBytes("Error: locator.click: no element matches '#submit'"));
        }

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
        }));

        // ⚠️ THE CLAIM. Not "false", not "true", and not a blank: the true
        // statement about a call whose answer nobody recorded.
        await Assert.That(text).Contains("no answer was recorded");
        await Assert.That(text).Contains("loading the page the process died on");

        // The failure beside it, with what failed and not a summary.
        await Assert.That(text).Contains("FAILED");
        await Assert.That(text).Contains("it failed with: Error: locator.click: no element matches '#submit'");

        // And the control: the call that worked says so, and stores nothing.
        await Assert.That(text).Contains("✓");
    }

    /// <summary>The numbered log lines of one page, for a stability comparison.</summary>
    /// <param name="text">The page.</param>
    /// <returns>Its entry lines.</returns>
    private static IReadOnlyList<string> Entries(string text) =>
        [.. text.Split('\n').Where(line => line.StartsWith("  ", StringComparison.Ordinal) && line.Contains(". 20", StringComparison.Ordinal))];

    [Test]
    public async Task ADirectoryThatIsNotASessionIsRefusedWithSomewhereToGo()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "never-a-session");
        _ = Directory.CreateDirectory(directory);

        var answer = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["why"] = "the suite reading back what this session did",
            ["session"] = directory,
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();

        var text = TextOf(answer);

        await Assert.That(text).Contains(SessionLayout.DataFileName);
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

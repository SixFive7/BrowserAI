// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// <c>browserai_destroy</c>'s two contracts: the directory stays <b>owned</b>
/// for as long as any of it is still on disk, and whatever it could not remove
/// is <b>named</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The second arm exists because a fast machine never reaches it.</b>
/// Destroy answers in two shapes — everything went, or a tally and a list of what
/// would not — and on a developer machine the first shape is the only one anybody
/// ever sees. <b>Since 2026-08-19 they carry different <c>isError</c> flags</b>
/// (<i>previously both were <c>isError: false</c></i>), which makes the shape a
/// fast machine never reaches also the one whose result code nothing local could
/// have observed. <c>FirefoxSessionTests</c> asserted
/// the tree was simply gone, passed nine local runs and failed three consecutive
/// CI runs on a four-core runner, where Firefox was still mapping its profile
/// when the answer was composed. <see cref="DestroyAnswer"/> now carries the
/// contract for both tests, and <see cref="ADestroyThatCannotRemoveEverythingNamesWhatSurvivedAndSaysHowMany"/>
/// provokes the survivor arm deterministically — with a handle this test holds
/// itself, needing no browser and no slow machine — so the arm CI takes is
/// exercised on every run rather than only on the runs that fail.
/// </para>
/// <para>
/// <b>Why this is not asserted on the outcome.</b> A destroy that released the
/// lock first and a destroy that held it to the end leave byte-identical trees —
/// an empty parent, or the same list of survivors. The difference is only
/// visible <i>while it runs</i>, so the assertion is made by a peer that tries to
/// take the directory throughout, using the same call the sweep uses
/// (<see cref="SessionLock.TryHoldUnowned"/>) and therefore getting the same
/// kernel answer a second BrowserAI process would.
/// </para>
/// <para>
/// <b>The peer waits for the delete to begin, and that is what makes the test
/// exact rather than lucky.</b> Before the destroy re-takes the directory there
/// is a legitimate unowned interval — the live session is released first — and a
/// probe landing there would block the destroy's own acquisition rather than
/// measure anything. So the peer probes nothing until the first planted file has
/// gone, which is proof the delete pass is already running and therefore that
/// ownership has already been proven. From that instant on, a directory that can
/// be taken is the defect and nothing else.
/// </para>
/// <para>
/// <b>The window is widened on purpose rather than waited for.</b> The defect
/// this was written against
/// ([the 2026-08-18 adversarial review](../../docs/reviews/2026-08-18-adversarial-locking.md))
/// released ownership, then walked the whole tree for a size, then deleted — so
/// the unowned interval was a full recursive walk of a Chromium profile wide.
/// The planted files reproduce that width without a browser: they sort before
/// <c>browserai.json</c>, so the delete pass removes every one of them before it
/// reaches the lock file.
/// </para>
/// <para>
/// <b>The green direction is a property, not a coincidence.</b> Nothing under the
/// session is removed except while <c>browserai.json</c> is held, and the last two
/// nodes go inside the per-directory gate that
/// <see cref="SessionLock.TryHoldUnowned"/> has to take itself. So the peer
/// cannot succeed at any point after the first file goes, at any planted size,
/// on any machine.
/// </para>
/// </remarks>
internal sealed class SessionDestroyTests
{
    /// <summary>
    /// How many files are planted at the session root, all sorting before
    /// <c>browserai.json</c>.
    /// </summary>
    /// <remarks>
    /// Enough that removing them is measurable work rather than one syscall, and
    /// small enough that planting them is not the cost of the test. The count
    /// sets how wide the broken code's window was; the fixed code has no window
    /// at any width.
    /// </remarks>
    private const int PlantedFiles = 2_000;

    /// <summary>The first planted name, and therefore the first node the delete pass removes.</summary>
    private const string FirstPlantedFile = "aaa-00000.bin";

    /// <summary>How long the peer waits between probes.</summary>
    /// <remarks>
    /// <b>A sleep rather than a spin, and it is not a promptness bound.</b> The
    /// suite runs every test at once on purpose; a thread spinning on a named
    /// mutex and a file open for the length of a directory delete is this test
    /// paying for its evidence with everybody else's timing, and it showed —
    /// the most start-up-sensitive test in the suite began meeting its own
    /// budget. It costs nothing here, because what the peer is watching for is
    /// the removal of two thousand files: hundreds of probes land inside that
    /// interval at a millisecond apiece, and against the pre-fix code the count
    /// they returned was in the thousands.
    /// </remarks>
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(1);

    [Test]
    public async Task DestroyKeepsTheDirectoryOwnedForAsLongAsAnyOfItIsStillOnDisk()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "destroyed-while-a-peer-watches");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "destroyed while a peer tries to take the directory out from under it",
        });

        for (var i = 0; i < PlantedFiles; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, $"aaa-{i.ToString("D5", CultureInfo.InvariantCulture)}.bin"),
                "planted so the delete pass has real work to do");
        }

        var firstToGo = Path.Combine(directory, FirstPlantedFile);
        var location = SessionPath.For(directory);

        using var stop = new CancellationTokenSource();
        var takenWhileTheTreeWasComingDown = 0;

        var peer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested && File.Exists(firstToGo))
            {
                // Nothing is probed yet. See the type's remarks: the interval
                // before the destroy re-takes the directory is legitimately
                // unowned, and probing it would contend with the acquisition
                // this test needs to succeed.
                Thread.Sleep(ProbeInterval);
            }

            while (!stop.IsCancellationRequested)
            {
                Thread.Sleep(ProbeInterval);

                if (SessionLock.TryHoldUnowned(location, out var hold) is not null)
                {
                    continue;
                }

                hold?.Dispose();
                _ = Interlocked.Increment(ref takenWhileTheTreeWasComingDown);
            }
        });

        var destroyed = await CallAsync(rig, SessionToolSurface.Destroy, new JsonObject
        {
            ["why"] = "the suite exercising this call",
            ["directory"] = directory,
        });

        await stop.CancelAsync();
        await peer;

        // The invariant, asserted first because it is the diagnosis: everything
        // else below is a consequence of a peer having taken the directory.
        await Assert.That(takenWhileTheTreeWasComingDown).IsEqualTo(0);

        await Assert.That((bool?)destroyed["isError"]).IsNotEqualTo(true);
        await Assert.That(Directory.Exists(directory)).IsFalse();

        // The positive control, because a probe that can never answer "yes"
        // reports zero for a reason that has nothing to do with the invariant.
        // The same call, against a session directory whose holder has let go,
        // succeeds.
        var released = Path.Combine(sessions.Root, "a-session-nobody-is-holding");
        _ = Directory.CreateDirectory(released);

        var control = SessionPath.For(released);
        var taken = SessionLock.TryAcquire(
            control,
            new SessionLockRequest
            {
                Browser = ProvisionedBrowsers.Chromium,
                Purpose = "taken and released, so the peer's own probe has something to say yes to",
            },
            NullLogger.Instance);

        await Assert.That(taken.Acquired).IsNotNull();
        taken.Acquired?.Dispose();

        var refusal = SessionLock.TryHoldUnowned(control, out var held);

        held?.Dispose();
        await Assert.That(refusal).IsNull();
    }

    /// <summary>
    /// A destroy that could not remove everything says so, says how many, and
    /// names them — and still reports success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The survivor is a handle this test holds, which is what makes the arm
    /// deterministic.</b> No browser, no timing, no slow machine: a file opened
    /// <c>FileShare.None</c> cannot be unlinked, so the walk records it and
    /// records the directory above it that therefore cannot go either. Against a
    /// real browser the same arm is reached by the kernel lagging behind a
    /// process that has already exited, which is not something a test can ask
    /// for.
    /// </para>
    /// <para>
    /// ⚠️ <b><c>isError</c> is <see langword="true"/>, changed 2026-08-19, and
    /// that is the decision being asserted rather than an accident of the
    /// code.</b> <i>Previously it stayed <see langword="false"/>, defended as: a
    /// destroy that removed a nine-thousand-file profile and could not remove
    /// eleven locked files has done what it was asked, and failing the call
    /// would throw away the report of the nine thousand and tell the model to
    /// retry something that mostly worked.</i> The maintainer took the other
    /// side (<c>QUESTIONS.md</c> §11): a call that did not entirely do the thing
    /// it is named for must not be indistinguishable, to a model scanning result
    /// shapes, from one that did. <b>The retry the old defence predicted is
    /// answered by the text rather than by the flag</b> — the arm now says the
    /// session is already destroyed, says not to call the tool again, and says
    /// what to do instead, which is what the assertions below hold it to. A
    /// naming nobody checks is still how either flag becomes a lie.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADestroyThatCannotRemoveEverythingNamesWhatSurvivedAndSaysHowMany()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "destroyed-while-something-holds-a-file");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "destroyed while this test holds one of its files open",
        });

        var held = Path.Combine(directory, "something-still-has-this-open.bin");
        JsonObject destroyed;

        await using (var handle = new FileStream(held, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await handle.WriteAsync("held open for the whole of the destroy"u8.ToArray());
            await handle.FlushAsync();

            destroyed = await CallAsync(rig, SessionToolSurface.Destroy, new JsonObject
            {
                ["why"] = "the suite exercising this call",
                ["directory"] = directory,
            });

            // Error-shaped on purpose: see the remarks.
            await Assert.That((bool?)destroyed["isError"]).IsTrue();

            var answer = TextOf(destroyed);

            // The whole contract, through the same routine FirefoxSessionTests
            // uses against a real Firefox.
            await DestroyAnswer.AccountsForWhatItLeftAsync(answer, (bool?)destroyed["isError"], directory);

            // ⚠️ AND THE ERROR CARRIES THE THREE THINGS THAT MAKE IT ACTIONABLE,
            // which is the refinement the decision rests on rather than a
            // restatement of the prose. An error that only said "N items could
            // not be removed" would invite exactly the retry the objection
            // predicted -- and that retry finds no session and is refused, which
            // is a worse answer than the truthful one this arm used to give.
            // Asserted on the product's own tool name, so a rename moves both.
            await Assert.That(answer).Contains("IS destroyed").Because(answer);
            await Assert.That(answer).Contains($"Do NOT call {SessionToolSurface.Destroy}").Because(answer);
            await Assert.That(answer).Contains("wait for whatever still holds those files to exit").Because(answer);

            // ⚠️ AND THE SURVIVOR IT NAMED IS THE ONE THAT SURVIVED. Every
            // assertion above is satisfied by an answer that names some other
            // node under the session, and "N item(s) could not be removed" is
            // only actionable if the N are the right ones.
            var listed = DestroyAnswer.SurvivorsNamedIn(answer)?.Listed ?? [];

            await Assert.That(listed.Any(line => line.StartsWith(held, StringComparison.OrdinalIgnoreCase)))
                .IsTrue()
                .Because(answer);

            // And the directory above it, which the post-order walk reaches last
            // and reports as `<path>\: <why>` — the trailing separator is how a
            // directory that would not go is told apart from a file of the same
            // name. A caller told only about the file would not know the session
            // directory itself is still there.
            await Assert.That(listed.Any(line => line.StartsWith($"{directory}{Path.DirectorySeparatorChar}:", StringComparison.OrdinalIgnoreCase)))
                .IsTrue()
                .Because(answer);

            await Assert.That(File.Exists(held)).IsTrue();
        }

        // Released, and now the advice the answer gave -- "wait for whatever
        // still holds those files to exit and then delete them yourself" -- is a
        // thing that actually works.
        var afterRelease = ScratchDirectory.RemoveTree(directory);

        await Assert.That(string.Join(Environment.NewLine, afterRelease)).IsEmpty();
    }

    /// <summary>
    /// A destroy that could not remove more items than it will name says that
    /// the list was cut.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The defect this holds shut, found 2026-08-19.</b> The answer carried
    /// a complete tally and a listing capped at
    /// <see cref="SessionManager.SurvivorsNamed"/>, and <b>nothing in the text
    /// said it had been cut</b>. At 25 survivors a reader saw the number 25 and
    /// twenty lines; the only evidence of the other five was a subtraction nobody
    /// was asked to do, and this answer is written for a model, which will read
    /// twenty lines under a heading as the whole list. The cap itself is right
    /// and is unchanged — a thousand-line answer is not an improvement.
    /// </para>
    /// <para>
    /// <b>Deterministic, by the same means as its neighbour above:</b> one more
    /// handle than the cap allows, each held <c>FileShare.None</c> for the whole
    /// call, so the walk cannot unlink any of them. No browser and no slow
    /// machine. The session directory itself is a survivor too, which is why the
    /// tally lands comfortably past the cap rather than exactly on it.
    /// </para>
    /// <para>
    /// <b>The truncation note is read from the product</b>, through
    /// <see cref="SessionManager.TruncationNote"/> and
    /// <see cref="DestroyAnswer"/>, rather than re-typed here — a test holding
    /// its own copy of a sentence stops recognising the arm the day somebody
    /// rewords it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADestroyThatNamesFewerSurvivorsThanItCountsSaysTheListWasCut()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "destroyed-with-more-survivors-than-it-names");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "destroyed while this test holds more files open than the answer will name",
        });

        var held = new List<FileStream>();

        try
        {
            for (var index = 0; index <= SessionManager.SurvivorsNamed; index++)
            {
                var path = Path.Combine(directory, $"held-{index.ToString("D3", CultureInfo.InvariantCulture)}.bin");
                var handle = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

                held.Add(handle);
                await handle.WriteAsync("held open for the whole of the destroy"u8.ToArray());
                await handle.FlushAsync();
            }

            var destroyed = await CallAsync(rig, SessionToolSurface.Destroy, new JsonObject
            {
                ["why"] = "the suite exercising this call",
                ["directory"] = directory,
            });

            var answer = TextOf(destroyed);

            // The whole contract first, which now includes the note in both
            // directions and the isError flag in both directions.
            await DestroyAnswer.AccountsForWhatItLeftAsync(answer, (bool?)destroyed["isError"], directory);

            var survivors = DestroyAnswer.SurvivorsNamedIn(answer);

            await Assert.That(survivors is not null).IsTrue().Because(answer);

            // ⚠️ THE PREDICATE BEFORE THE NUMBER: items the walk could not
            // remove, which is the held files plus the directory above them.
            // Asserted as "more than the cap" rather than as an exact figure --
            // a temp file or a log beside them would change the tally and change
            // nothing about the property under test.
            await Assert.That(survivors!.Value.Stated).IsGreaterThan(SessionManager.SurvivorsNamed).Because(answer);
            await Assert.That(survivors.Value.Listed.Count).IsEqualTo(SessionManager.SurvivorsNamed).Because(answer);

            await Assert.That(answer).Contains(SessionManager.TruncationNote(survivors.Value.Stated));
        }
        finally
        {
            foreach (var handle in held)
            {
                await handle.DisposeAsync();
            }
        }

        var afterRelease = ScratchDirectory.RemoveTree(directory);

        await Assert.That(string.Join(Environment.NewLine, afterRelease)).IsEmpty();
    }

    /// <summary>
    /// The listing routine names everything up to the cap and says nothing about
    /// truncation until there is truncation to report.
    /// </summary>
    /// <remarks>
    /// <b>The boundary, asserted directly, because the three call sites cannot
    /// all reach it.</b> <see cref="SessionManager.Listing"/> also builds the
    /// reinstall's survivor list and the reinstall refusal's session list, and
    /// provoking twenty-one locked files inside a browser tree or twenty-one live
    /// sessions to make those arms reachable would cost minutes per assertion for
    /// a property that is a function of a list and an <c>int</c>. The end-to-end
    /// arm above proves the routine is wired to the answer; this proves the
    /// routine.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheListingSaysNothingAboutTruncationUntilItTruncates()
    {
        var cap = SessionManager.SurvivorsNamed;

        // Exactly at the cap: everything named, and nothing claimed about items
        // that do not exist. An off-by-one that noted a truncation here would
        // tell a caller to go looking for a twenty-first survivor.
        var complete = SessionManager.Listing(Items(cap));

        await Assert.That(complete.Split('\n').Length).IsEqualTo(cap);
        await Assert.That(complete).DoesNotContain("Only the first");

        // Empty, which is the reinstall refusal's shape when the index names no
        // live session of the family.
        await Assert.That(SessionManager.Listing([])).IsEmpty();

        // One past it, which is the smallest cut there is.
        var cut = SessionManager.Listing(Items(cap + 1));

        await Assert.That(cut.Split('\n').Length).IsEqualTo(cap + 1);
        await Assert.That(cut).EndsWith(SessionManager.TruncationNote(cap + 1));
        await Assert.That(cut).Contains("1 more are not named here");
        await Assert.That(cut).DoesNotContain($"item-{cap.ToString("D3", CultureInfo.InvariantCulture)}");

        // And well past it, so the note does arithmetic rather than repeating a
        // constant.
        await Assert.That(SessionManager.Listing(Items(cap + 5))).EndsWith(SessionManager.TruncationNote(cap + 5));
        await Assert.That(SessionManager.TruncationNote(cap + 5)).Contains("5 more are not named here");
    }

    /// <summary>
    /// A directory holding the record format this build does not read is
    /// refused by <c>browserai_destroy</c>, in the maintainer's own words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The tool that deletes trees must not delete one whose contents it
    /// cannot recognise.</b> <c>browserai_destroy</c> is safe because it refuses
    /// any directory that is not a BrowserAI session — that is the whole of what
    /// stops it being aimed at <c>Documents\</c> — and a directory holding a
    /// <c>browserai.json</c> is a BrowserAI session, just not one this build can
    /// open. Neither <i>not a session</i> nor <i>damaged</i> is true of it.
    /// </para>
    /// <para>
    /// <b>The sentence is asserted verbatim because it is a promise about who
    /// does the work.</b> Every other refusal in this product names a recovery
    /// BrowserAI can perform; this one names one it cannot, and the honest form
    /// of that is to say so in the first person rather than to offer a tool that
    /// will refuse in turn.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADirectoryHoldingTheOldRecordIsRefusedAndDestroySaysToRemoveItYourself()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "an-old-format-session");
        var location = SessionPath.For(directory);

        SessionLayout.Create(location);

        var legacy = Path.Combine(directory, SessionLayout.LegacyRecordFileName);

        await File.WriteAllTextAsync(legacy, """{"schemaVersion": 4, "purpose": [], "log": []}""");

        var answer = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Destroy,
            ["arguments"] = new JsonObject
            {
                ["directory"] = directory,
                ["why"] = "trying to clean up a session this build cannot read",
            },
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();

        var text = TextOf(answer);

        // ⚠️ THE MAINTAINER'S WORDING, VERBATIM.
        await Assert.That(text).Contains("I cannot clean this up — remove the entire directory yourself.");

        // With the format as the reason rather than damage, and no converter
        // offered.
        await Assert.That(text).Contains(SessionLayout.LegacyRecordFileName);
        await Assert.That(text).Contains("There is no converter");

        // ⚠️ AND NOTHING WAS TOUCHED. A refusal that had already started
        // deleting would leave the caller with half a directory and a sentence
        // telling them to remove the rest.
        await Assert.That(File.Exists(legacy)).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(directory, SessionLayout.ProfileFolderName))).IsTrue();
        await Assert.That(File.Exists(location.LockFile)).IsFalse();
        await Assert.That(File.Exists(location.DataFile)).IsFalse();
    }

    /// <summary>Indented lines shaped like the ones <c>TreeDelete</c> produces.</summary>
    /// <param name="count">How many.</param>
    /// <returns>The lines.</returns>
    private static string[] Items(int count) =>
        [.. Enumerable.Range(0, count).Select(index => $"  item-{index.ToString("D3", CultureInfo.InvariantCulture)}")];

    private static string TextOf(JsonObject answer) =>
        string.Join(
            "\n",
            (answer["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });
}

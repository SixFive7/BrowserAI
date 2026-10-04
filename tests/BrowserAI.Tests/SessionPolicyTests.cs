// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What BrowserAI decides about a browser call: which child it goes to, and the
/// one call it declines to make because it would never come back.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Rewritten 2026-08-18 (previously "the <c>(tool, mode)</c> decision:
/// classified exhaustively, deny-by-default, and correct while sessions of
/// different modes are being driven at once").</b> Seven of this file's eight
/// tests asserted a permission matrix that no longer exists. <b>Six were deleted
/// outright</b> and the seventh was inverted, instead of any of them being left
/// asserting a tautology -- a test that can no longer fail is worse than a gap,
/// because it reads as covered. The eighth, the concurrency arm, was reframed and
/// is the last test below. Across the suite that is <b>432 tests before and 428
/// after</b>: six deleted here plus one in <c>ErrorCatalogueTests</c>
/// (<c>AConfigAnswerCarryingSecretsIsWithheldRatherThanForwarded</c>, which
/// provoked the removed <c>Guard</c>), against three added. What went, and why
/// each was testing something that is gone:
/// </para>
/// <list type="bullet">
/// <item><c>EveryToolTheChildCanExposeCarriesAnExplicitClassification</c> and
/// <c>EveryToolTheProxyAdvertisesIsClassified</c> -- there is no classification
/// table to be exhaustive about. The change-detection they provided is the
/// [golden snapshot](../../upstream-snapshots/tools-list.json)'s job, and the
/// snapshot does it better: it diffs each tool's <c>inputSchema</c> as well as
/// its name, which a name-keyed table never saw.</item>
/// <item><c>EachModePermitsExactlyTheSurfaceTheTestsDeclare</c> and
/// <c>ThePolicyRowsAndTheModeTableCannotDriftApart</c> -- there are no policy rows
/// left to drift from the mode table. The first is replaced below by the counts
/// that survive, which are nearly the whole surface.</item>
/// <item><c>AToolNobodyClassifiedIsRefusedInEveryMode</c> and
/// <c>AnUnclassifiedToolInTheChildsListIsRefusedOverTheWire</c> -- the
/// <c>(tool, mode)</c> matrix is gone, so the second was inverted to assert that
/// a tool this build had never heard of was <i>forwarded</i>. ⚠️ <b>That
/// inversion was itself inverted on 2026-08-26</b>: deny-by-default came back as
/// a VERDICT and not as a permission, and
/// <c>AToolThisBuildHasNeverJudgedIsRefusedRatherThanForwarded</c> below says why
/// the 2026-08-18 reasoning does not reach it.</item>
/// <item><c>AStorageToolOnAHeadlessSessionIsRefusedWithTextNamingPersistent</c> --
/// BrowserAI no longer refuses it. A headless session's own child is launched
/// without the <c>storage</c> capability, so the storage tools do not exist in
/// it and upstream answers; that is a property of
/// <c>BrowserConfiguration.ForSession</c> and <c>ConfigRoundTripTests</c> owns
/// it.</item>
/// </list>
/// <para>
/// <b>Why the matrix went.</b> It was described as the charter's security
/// trade-off made true and it was never a boundary against the caller: the
/// calling agent chooses the session directory, the profile and its cookie
/// database are created inside it, and the agent runs as the same Windows user,
/// so DPAPI decrypts for it. Refusing <c>browser_cookie_list</c> to a caller
/// holding file tools costs a lookup per call and buys one extra step.
/// <b>Measured 2026-08-18</b>, from a second process as the same user against a
/// session this product configured -- <c>CryptUnprotectData</c> and AES-256-GCM,
/// no App-Bound Encryption
/// ([kb](../../kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one----measured-2026-08-18)).
/// </para>
/// <para>
/// ⚠️ <b>Since 2026-08-26 the decision this class is about is a FILE.</b>
/// <c>tool-verdicts.json</c> carries a row per tool -- <c>allow</c>, <c>deny</c>
/// with the reason a caller reads, or <c>answer</c> -- and a name with no row is
/// refused. Three arms below are about the mechanism and not about any one
/// tool, and they use rig copies of that file so the product's own deny set is
/// left alone; <c>ToolVerdictTests</c> owns the file itself and its agreement
/// with the golden snapshot. *(Was "stays at exactly one" until 2026-09-15, when
/// <c>browser_webmcp_call</c> made it two.)*
/// </para>
/// <para>
/// <b>What is asserted instead is what is actually true.</b> A call names its
/// session or it is refused -- that is <b>routing</b>, and the concurrency arm
/// below drives it across sessions being opened and destroyed at the same time.
/// And <c>browser_annotate</c> is <b>not advertised at all</b>, because it would
/// <b>hang</b>: that is a liveness claim and is asserted as one -- and
/// <b>measured on 2026-08-18</b>, three runs against a real headless child: a
/// visible window took the foreground within 1.2 s every time and the call was
/// still silent 90 s later
/// ([kb](../../kb/playwright/tools-and-artifacts.md#what-browser_annotate-actually-does----measured-2026-08-18)).
/// <b>The measurement is deliberately not an arm of this class</b>: asserting
/// that a call does not return means spending the budget waiting for it, with a
/// focus-stealing window on the developer's screen throughout.
/// </para>
/// <para>
/// ⚠️ <b>Amended 2026-08-18, later the same day (previously "<c>browser_annotate</c>
/// is refused on a windowless session ... permitted on the two modes that open
/// one").</b> The same measurement said the daemon is detached, per-user and
/// writes into <c>%TEMP%</c>, and that the call is unbounded on every mode -- so
/// the tool is withheld from <c>tools/list</c> in every mode and refused if a
/// caller names it anyway. Both halves are asserted below, and against a child
/// double that would happily answer the call.
/// </para>
/// </remarks>
internal sealed class SessionPolicyTests
{
    /// <summary>
    /// What one session permits of the surface BrowserAI advertises, written
    /// down and not computed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-09-15 to 70 of 70 (previously 68 of 68, corrected
    /// 2026-08-20 from three rows, 58 / 58 / 58 of 58, one per session mode;
    /// 58 / 59 / 59 of 59 before that, and 41 / 41 / 58 before that, measured
    /// 2026-08-16 against the five-class permission matrix).</b> Session modes
    /// were deleted, so there is one row and not three; and every capability
    /// is now granted to every session, which put ten previously-unreachable
    /// tools into the surface -- <c>network</c>'s four, <c>pdf</c>'s one and
    /// <c>testing</c>'s five. The 2026-09-15 move is upstream's and not
    /// ours: <c>@playwright/mcp</c> 0.0.80 added
    /// <c>browser_start_recording</c> and <c>browser_stop_recording</c>, both
    /// judged <c>allow</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected again 2026-09-15, later the same day, to 71 of 71
    /// (previously "Of the 71 tools a fully-capable child exposes, BrowserAI's
    /// <c>tools/list</c> carries <b>70</b>: <c>browser_annotate</c> is withheld,
    /// and it is still the only one").</b> <c>@playwright/mcp</c> 0.0.81 added
    /// <c>browser_webmcp_list</c> and <c>browser_webmcp_call</c> -- both
    /// <c>core</c>, so both unconditional -- taking the exposable surface from 71
    /// to <b>73</b>. The two were judged in opposite directions: the list
    /// <c>allow</c>, the call <c>deny</c> on liveness, because it runs a tool the
    /// page supplies and waits for it with no timeout. So the denominator moved
    /// by two, the numerator by one, and <b>the withheld set is two and not
    /// one for the first time</b> -- which is why the arithmetic below reads
    /// <c>Advertises + 2</c> and the named hole is a loop.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-09-17 to 72 of 74 (previously 71 of 73).</b> The
    /// <see href="https://github.com/microsoft/playwright/pull/42673">dated
    /// <c>playwright-core</c> override</see> added
    /// <c>browser_emulate_media</c> -- <c>core</c>, so unconditional -- and it was
    /// judged <c>allow</c>, so numerator and denominator moved together and
    /// <see cref="Withholds"/> did not move at all. That is the ordinary shape;
    /// the paragraph above records the one time it was not.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-03 to 70 of 72 (previously 71 of 72), and it is
    /// the first move in this constant that a decision made and not an
    /// upstream release.</b> The maintainer removed <c>browser_resume</c>, so
    /// the denominator held still and the numerator lost one, and
    /// <see cref="Withholds"/> went 1 → 2 beside it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-04 to 65 of 72 (previously 70 of 72)</b>, the
    /// second move a decision made: the maintainer's Q365.2 a dropped the five
    /// test-writing helpers, so the numerator lost five and
    /// <see cref="Withholds"/> went 2 → 7 beside it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-04 a second time, to 64 of 72 (previously 65 of
    /// 72)</b>: the maintainer's Q365.1 a denied <c>browser_set_storage_state</c>,
    /// which wipes the profile's cookies and site storage before it loads a saved
    /// file, so the numerator lost one and <see cref="Withholds"/> went 7 → 8.
    /// </para>
    /// <para>
    /// <b>Written down and not derived, for the reason the old table was:</b>
    /// derived from the product's own decision it would agree with it by
    /// construction and could never fail. This one still can -- a refusal
    /// reintroduced anywhere, or a surface that changed size.
    /// </para>
    /// </remarks>
    private const int Advertises = 64;

    /// <summary>
    /// How many tools this build withholds, written down beside
    /// <see cref="Advertises"/> and for the same reason.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Two since 2026-10-03, and this time the maintainer decided it</b>:
    /// <c>browser_resume</c> joined <c>browser_annotate</c> as a <c>deny</c>
    /// row, because it is Playwright's debugger control and its name is two
    /// letters from BrowserAI's own <c>browserai_resume</c>. <i>Previously
    /// "One again since 2026-09-21", which follows.</i>
    /// ⚠️ <b>One again since 2026-09-21</b>, and <b>nobody decided that</b>:
    /// <c>@playwright/mcp</c> 0.0.82 marked <c>browser_webmcp_call</c>
    /// <c>skillOnly</c>, so it left the exposed surface and its verdict row was
    /// deleted as a judgement about nothing. The deny's reasoning is preserved
    /// in <c>tool-verdicts.json</c> and <c>upstream-review.json</c> because the
    /// tool can come back. <see cref="Advertises"/> moved 72 → 71 with it, so
    /// the pair went 74 → 72 and both halves moved for the same cause -- which
    /// is exactly the shape the paragraph below says this pair exists to catch,
    /// arriving from upstream and not from a refusal being reintroduced.
    /// <i>Previously <b>two since 2026-09-15</b>.</i> It is stated and not read off
    /// <c>RepositoryVerdicts.Count</c> here: the pair
    /// <c>Advertises</c> + <c>Withholds</c> is the whole claim this class makes
    /// about the size of the surface, and reading either half out of the file
    /// the claim is about would make it agree with itself.
    /// </remarks>
    // ⚠️ Seven since 2026-10-04 (previously two): Q365.2 a, the maintainer's
    // words of 2026-10-03 verbatim, "Drop browser_verify_element_visible,
    // browser_verify_text_visiblem, browser_verify_list_visible,
    // browser_verify_value and browser_generate_locator per your recommendation."
    // ⚠️ Eight since 2026-10-04 a second time (previously seven): Q365.1 a, the
    // maintainer's words verbatim, "Q365.1 a" -- browser_set_storage_state is
    // denied, because it replaces the profile's logins with whatever an older
    // saved file holds.
    private const int Withholds = 8;

    /// <summary>
    /// The three sessions the concurrency arm drives at once.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Named and not derived from anything, 2026-08-20 (previously one
    /// session per row of <c>SessionModes.All</c>).</b> Modes are gone. Three is
    /// what the arithmetic in that arm is written against -- 25 rounds × 3
    /// sessions × 4 probes -- and naming them here instead of looping over a
    /// product list is what stops the denominator moving when something
    /// unrelated does.
    /// </remarks>
    private static readonly string[] Concurrent = ["alpha", "beta", "gamma"];

    [Test]
    public async Task ASessionPermitsEveryToolItAdvertisesAndTheOneThatWouldHangIsNotAdvertised()
    {
        var everything = UpstreamSurface.For(BrowserConfiguration.GrantedCapabilities);
        var advertised = everything.Where(tool => !RepositoryVerdicts.Committed.IsWithheldFromTheSurface(tool)).ToList();

        // The denominators are stated before the numerator, and there are two of
        // them: a fully-capable child exposes 72 tools, BrowserAI advertises 70
        // of them, and every session permits all 70. *(Corrected 2026-10-03,
        // previously "exposes 73 tools, BrowserAI advertises 71 of them", whose
        // first figure had disagreed with the constants above since 2026-09-21.)*
        await Assert.That(everything.Count).IsEqualTo(Advertises + Withholds);
        await Assert.That(advertised.Count).IsEqualTo(Advertises);
        await Assert.That(advertised.Count(tool => RepositoryVerdicts.Committed.Decide(tool).IsAllowed)).IsEqualTo(Advertises);

        // And the file agrees with the number written above, which is what stops
        // the two constants being satisfied by a surface that lost a tool at the
        // same moment it gained a denial.
        await Assert.That(RepositoryVerdicts.Count).IsEqualTo(Withholds);

        // The named holes, individually, because a count is satisfied by the
        // wrong tool as easily as by the right one. Each is in the child's
        // surface and out of ours, which is the whole of the change.
        //
        // ⚠️ A loop since 2026-09-15 (previously the single `TheOneDenial`).
        foreach (var denial in RepositoryVerdicts.TheDenials)
        {
            await Assert.That(everything).Contains(denial.Name);
            await Assert.That(advertised).DoesNotContain(denial.Name);

            // And the call is refused as well as unadvertised, which is the half
            // a filtered list cannot do: a model that knows the name from
            // upstream can still send it.
            await Assert.That(RepositoryVerdicts.Committed.Decide(denial.Name).IsAllowed).IsFalse();
        }

        // The tools the old matrix turned on are permitted now. Asserted, not
        // left implied: these three are the whole of what that removal
        // changed, and a reader who learned the old behaviour needs to see it
        // stated.
        await Assert.That(Allows("browser_run_code_unsafe")).IsTrue();
        await Assert.That(Allows("browser_cookie_list")).IsTrue();
        await Assert.That(Allows("browser_get_config")).IsTrue();
    }

    /// <summary>
    /// A tool a child advertises that this build has never judged is refused,
    /// and the child never hears about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>INVERTED 2026-08-26 (previously
    /// <c>AToolThisBuildHasNeverHeardOfIsForwardedRatherThanRefused</c>, whose
    /// own comment said it was "the test that proves the removal actually
    /// happened").</b> Deny-by-default is back, and the old claim is no longer
    /// true of this product -- so the arm asserts the new policy instead of being
    /// deleted, because the case it covers did not go anywhere.
    /// </para>
    /// <para>
    /// <b>What was removed on 2026-08-18 is still removed, and this is not it.</b>
    /// That was a <c>(tool, mode)</c> PERMISSION matrix, deleted because it was
    /// never a boundary against a caller who owns the session directory and reads
    /// the profile inside it as the same user -- and every word of that reasoning
    /// still holds. A verdict is a different question: it decides whether a name
    /// this build has never been told about is worth <b>starting a browser</b>
    /// for. Upstream creates the browser context before it looks a tool name up,
    /// so the forwarded call the old arm asserted would launch a browser to be
    /// told there is nothing to run -- and would echo the caller's own string back
    /// into model-facing text on the way out. Neither is a permission and neither
    /// was in scope on 2026-08-18.
    /// </para>
    /// <para>
    /// <b>The child double would answer, which is what makes the claim real.</b>
    /// A proxy that forwarded would visibly reach it, and the assertion below is
    /// on the child's own call log and not on the shape of the answer.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AToolThisBuildHasNeverJudgedIsRefusedRatherThanForwarded()
    {
        const string FromTheFuture = "browser_a_tool_from_the_future";

        await using var sessions = RigSessionEnvironment.Create(child => child.Tools[FromTheFuture] = new FakeToolBehaviour());

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult =
                """{"tools":[{"name":"browser_navigate","description":"Navigate to a URL","inputSchema":{"type":"object","properties":{}}},{"name":"browser_a_tool_from_the_future","description":"A tool no build of BrowserAI has ever judged","inputSchema":{"type":"object","properties":{}}}]}""",
            sessions: sessions);

        var directory = Path.Combine(sessions.Root, "a-tool-from-the-future");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "meets a tool from the future",
        });

        var answer = await CallAsync(rig, FromTheFuture, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite exercising this call",
        });

        // ⚠️ The child was NOT asked, which is the whole claim and the whole of
        // what changed.
        await Assert.That(sessions.SessionChildren.Any(child =>
            child.ToolCallsReceived.Contains(FromTheFuture, StringComparer.Ordinal))).IsFalse();

        await Assert.That((bool?)answer["isError"]).IsTrue();
        await Assert.That(TextOf(answer)).IsEqualTo(SessionErrors.ToolHasNoVerdict());

        // It is still ADVERTISED, because a gap is not a decision -- and the
        // build is red about the gap on the same run, which is what bounds it.
        var advertised = await rig.Client.RoundTripAsync("tools/list", new JsonObject());

        await Assert.That(advertised.ToJsonString()).Contains(FromTheFuture);

        // ⚠️ AND THE REFUSAL HAS TO SURVIVE THE TWO LINES ABOVE, which is
        // what this asserts and what the sentence did not do until 2026-09-15.
        // The name IS in tools/list -- the assertion immediately above is the
        // proof -- and the refusal used to answer "every tool in that list
        // reaches the browser, and a name that is not in it never will". Read
        // against the list a caller can see, that is an instruction to send the
        // same name again, and a model that believes it retries until something
        // else stops it. Asserted as LITERALS and not against
        // SessionErrors.ToolHasNoVerdict(), because comparing a sentence to the
        // method that produces it cannot tell true from false; these say the
        // caller is told the fault is the build's and that a retry fails the
        // same way, and that the disproved clause is gone.
        //
        // ⚠️ Shortened 2026-10-04 (previously asserted "retrying it will fail"),
        // when a name BrowserAI does not have at all got a refusal of its own
        // and this one was left to the one case it is true of: a name the list
        // carries and the verdicts file does not.
        await Assert.That(TextOf(answer)).Contains("defect in this build");
        await Assert.That(TextOf(answer)).Contains("refused the same way every time");
        await Assert.That(TextOf(answer)).DoesNotContain("every tool in that list reaches the browser");
    }

    /// <summary>
    /// A name BrowserAI does not have -- in its own namespace or anybody's -- is
    /// told so plainly, with a session or without one, and recorded on the
    /// session it named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Decided 2026-10-03 by the maintainer, in his words: <i>"Calls to a tool
    /// BrowserAI doesn't have: a) Yes, in the same lane."</i></b> Until then such a
    /// call met the verdict door's sentence for a tool with no verdict, which called
    /// it a gap a human had to adjudicate and told the caller to stop: wrong advice
    /// for a list from another server or a name a model made up. And with no
    /// session it met <i>"this needs a session"</i> for an upstream-looking name,
    /// which sends a caller to supply one for a tool that is not there.
    /// </para>
    /// <para>
    /// ⚠️ <b>The short-circuit in front of the door was a PREFIX test until
    /// 2026-08-26</b>, so <c>browserai_zzz</c> wrote no log row while an unjudged
    /// <i>upstream</i> name was recorded on the session it named. Both are
    /// recorded now, by the same refusal; with no resolvable session there is
    /// still nowhere to write one, and that half is asserted too.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ANameBrowserAiDoesNotHaveIsToldSoPlainlyWithOrWithoutASession()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "a-name-that-does-not-exist");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "meets names this BrowserAI does not have",
        });

        foreach (var invented in new[] { "browserai_zzz", "browser_frobnicate" })
        {
            var answer = await CallAsync(rig, invented, new JsonObject
            {
                ["session"] = directory,
                ["why"] = "the suite exercising this call",
            });

            await Assert.That((bool?)answer["isError"]).IsTrue();
            await Assert.That(TextOf(answer)).IsEqualTo(SessionErrors.ToolDoesNotExist(invented));

            // The maintainer's own wording, Q371.6 c, as literals: the name, that
            // nothing ran, the tool list as the place to look, and why a tool the
            // caller expected may be missing from it.
            await Assert.That(TextOf(answer)).StartsWith($"BrowserAI has no tool '{invented}', so nothing ran. Use the tools in your tool list.");
            await Assert.That(TextOf(answer)).Contains("reconnects BrowserAI or starts a new conversation");

            // No history and no guess: no other name is suggested.
            await Assert.That(TextOf(answer)).DoesNotContain("did you mean");
            await Assert.That(TextOf(answer)).DoesNotContain("renamed");

            // Nothing reached a child, and the record keeps the caller's own
            // string, because "what did it try to call" is what a reader wants.
            await Assert.That(sessions.SessionChildren.Any(child =>
                child.ToolCallsReceived.Contains(invented, StringComparer.Ordinal))).IsFalse();

            var recorded = RecordedSession.LogOf(directory).Where(row => row.Tool == invented).ToList();

            await Assert.That(recorded.Count).IsEqualTo(1);
            await Assert.That(recorded[0].Outcome).IsEqualTo(SessionStore.Failed);
            await Assert.That(recorded[0].Failure!).IsEqualTo(SessionErrors.ToolDoesNotExist(invented));
            await Assert.That(recorded[0].Why).IsEqualTo("the suite exercising this call");

            // ⚠️ With no session: the same answer, and not "this needs a
            // session", which would send a caller to supply one for a tool that
            // is not there. Nothing is recorded, because there is nowhere to.
            var noSession = await CallAsync(rig, invented, new JsonObject { ["why"] = "the suite exercising this call" });

            await Assert.That((bool?)noSession["isError"]).IsTrue();
            await Assert.That(TextOf(noSession)).IsEqualTo(SessionErrors.ToolDoesNotExist(invented));
            await Assert.That(RecordedSession.LogOf(directory).Count(row => row.Tool == invented)).IsEqualTo(1);
        }

        // ⚠️ THE POSITIVE CONTROLS. A real authored tool still answers, and an
        // ordinary browser tool with no session still gets the session-missing
        // sentence, so the plain answer is narrow and not a replacement.
        var listed = await CallAsync(rig, SessionToolSurface.List, new JsonObject { ["directory"] = sessions.Root });

        await Assert.That((bool?)listed["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(listed)).Contains(directory);

        var browserToolNoSession = await CallAsync(rig, "browser_navigate", new JsonObject { ["why"] = "the suite exercising this call" });

        await Assert.That(TextOf(browserToolNoSession)).IsEqualTo(SessionErrors.SessionMissing("browser_navigate"));
    }

    /// <summary>
    /// The purpose tool is <c>browserai_change_purpose</c>, and the name it had
    /// before is a tool this BrowserAI does not have.
    /// </summary>
    /// <remarks>
    /// <b>Decided 2026-10-03 by the maintainer, in his words: <i>"rename
    /// browserai_set_purpose to browserai_change_purpose"</i></b>, with no alias
    /// and no mention of the old name in any model-facing text: a call to it is
    /// answered as any unknown tool is.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePurposeToolIsChangePurposeAndTheOldNameIsAToolBrowserAiDoesNotHave()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var advertised = await rig.Client.RoundTripAsync("tools/list", new JsonObject());
        var names = (advertised["tools"]?.AsArray() ?? []).Select(tool => (string?)tool?["name"] ?? string.Empty).ToList();

        await Assert.That(names).Contains("browserai_change_purpose");
        await Assert.That(names).DoesNotContain("browserai_set_purpose");
        await Assert.That(advertised.ToJsonString()).DoesNotContain("set_purpose");

        var old = await CallAsync(rig, "browserai_set_purpose", new JsonObject
        {
            ["session"] = rig.Session!,
            ["purpose"] = "a purpose the old name must not record",
            ["why"] = "the suite calling the tool by its old name",
        });

        await Assert.That((bool?)old["isError"]).IsTrue();
        await Assert.That(TextOf(old)).IsEqualTo(SessionErrors.ToolDoesNotExist("browserai_set_purpose"));

        var changed = await CallAsync(rig, "browserai_change_purpose", new JsonObject
        {
            ["session"] = rig.Session!,
            ["purpose"] = "the purpose the new name records",
            ["why"] = "the suite calling the tool by its name",
        });

        await Assert.That((bool?)changed["isError"]).IsNotEqualTo(true);
        await Assert.That(SessionLock.ReadRecord(SessionPath.For(rig.Session!))!.Purpose).IsEqualTo("the purpose the new name records");
    }

    [Test]
    public async Task TheAnnotationToolIsAbsentFromTheSurfaceAndRefusedIfNamedAnyway()
    {
        // ⚠️ Rewritten 2026-08-18 (previously
        // TheAnnotationToolIsRefusedWhereNoWindowWasPromisedAndForwardedWhereOneWas,
        // which asserted the headed arm FORWARDED the call and got the child's
        // answer back). It no longer does, and the child double below is what
        // makes that a real claim and not a missing case: it answers the
        // tool happily, so a proxy that forwarded would visibly succeed here.
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools[RepositoryVerdicts.ADenial.Name] = new FakeToolBehaviour
            {
                RawResult = """{"content":[{"type":"text","text":"the human drew something"}]}""",
            });

        // The surface child answers with upstream's own committed list, so the
        // absence asserted below is a filter and not a double that never had
        // the tool.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult(),
            sessions: sessions);

        // The surface half, off the wire: the child advertises it, BrowserAI
        // does not. Both halves matter -- "absent from our list" is satisfied
        // vacuously by a child that never had it.
        var childsOwn = rig.SurfaceChild.ToolsListResult;
        var advertised = await rig.Client.RoundTripAsync("tools/list", new JsonObject());

        var names = (advertised["tools"]?.AsArray() ?? [])
            .Select(tool => (string?)tool?["name"] ?? string.Empty)
            .ToList();

        await Assert.That(childsOwn).Contains(RepositoryVerdicts.ADenial.Name);
        await Assert.That(names).DoesNotContain(RepositoryVerdicts.ADenial.Name);
        await Assert.That(names.Count).IsGreaterThan(10);

        // And nothing of it is left in the surface for a model to read: no
        // description mentioning it, no note saying it would refuse.
        await Assert.That(advertised.ToJsonString()).DoesNotContain(RepositoryVerdicts.ADenial.Name);

        // The call half, on a session with a window and one without, because
        // the refusal used to be keyed on exactly that and a single session
        // could not say it is no longer.
        foreach (var headed in new[] { false, true })
        {
            var directory = Path.Combine(sessions.Root, $"annotate-headed-{headed}");

            _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = directory,
                ["purpose"] = $"a headed={headed} session that reaches for the annotation tool by name",
                ["headed"] = headed,
            });

            var callsBefore = sessions.SessionChildren.Sum(child =>
                child.ToolCallsReceived.Count(tool => tool == RepositoryVerdicts.ADenial.Name));

            var refused = await CallAsync(rig, RepositoryVerdicts.ADenial.Name, new JsonObject { ["session"] = directory, ["why"] = "the suite exercising this call" });
            var text = TextOf(refused);

            await Assert.That((bool?)refused["isError"]).IsTrue();

            // ⚠️ SINCE 2026-10-04 A DENIED TOOL IS ANSWERED LIKE ANY TOOL THIS
            // BROWSERAI DOES NOT HAVE, under the maintainer's directive of
            // 2026-10-03 that a tool BrowserAI does not offer should look to the
            // model like any other it does not have. Previously the answer was
            // the deny row's own `why` behind "is deliberately NOT in this server's
            // tools/list", and this arm asserted it said liveness. The `why` stays
            // in tool-verdicts.json as the human record.
            await Assert.That(text).IsEqualTo(SessionErrors.ToolDoesNotExist(RepositoryVerdicts.ADenial.Name));

            // Nothing reached the child: a refusal that forwarded first and hid
            // the answer would still have hung.
            await Assert.That(sessions.SessionChildren.Sum(child =>
                child.ToolCallsReceived.Count(tool => tool == RepositoryVerdicts.ADenial.Name))).IsEqualTo(callsBefore);
        }
    }

    /// <summary>
    /// Playwright's <c>browser_resume</c> is absent from the surface and refused
    /// if named, and the refusal gives the measured reason, liveness.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-03 at the maintainer's decision</b>, in his words: "I am
    /// leaning to remove playwrights own browser_resume and pause and similar from
    /// the tool list (and not pass them through). I do not see the utility and I do
    /// see a lot of possible confusion with our own resumtion tech." and then "P6
    /// a", which keeps the removal to this one tool. It is Playwright's debugger
    /// control: it releases a paused page and then waits for the next pause or for
    /// the browser to close.
    /// </para>
    /// <para>
    /// <b>By name, against the shipped file, because nothing else here fails if
    /// the row goes back to <c>allow</c>.</b> The arm below proves a deny row is
    /// read from the file, and the loops over <c>RepositoryVerdicts.TheDenials</c>
    /// prove every shipped denial is absent from the surface; both stay green with
    /// one row fewer to walk. <b>Planted red 2026-10-03</b> against the file as it
    /// stood, with the row still <c>allow</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDebuggerResumeToolIsAbsentFromTheSurfaceAndAnsweredAsAToolBrowserAiDoesNotHave()
    {
        const string DebuggerResume = "browser_resume";

        // The session child would answer it, so a proxy that forwarded would
        // visibly succeed here instead of failing for some other reason.
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools[DebuggerResume] = new FakeToolBehaviour());

        // The surface child answers with upstream's own committed list, so the
        // absence asserted below is a filter and not a double that never had
        // the tool.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult(),
            sessions: sessions);

        var advertised = await rig.Client.RoundTripAsync("tools/list", new JsonObject());

        var names = (advertised["tools"]?.AsArray() ?? [])
            .Select(tool => (string?)tool?["name"] ?? string.Empty)
            .ToList();

        // Not vacuous: upstream's own list carries it, and BrowserAI's own
        // resume tool is still advertised beside the gap.
        await Assert.That(rig.SurfaceChild.ToolsListResult).Contains(DebuggerResume);
        await Assert.That(names).DoesNotContain(DebuggerResume);
        await Assert.That(names).Contains(SessionToolSurface.Resume);

        // Nothing of it is left for a model to read: no entry, no description
        // naming it.
        await Assert.That(advertised.ToJsonString()).DoesNotContain(DebuggerResume);

        var directory = Path.Combine(sessions.Root, "debugger-resume");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "reaches for Playwright's debugger resume tool by name",
        });

        var callsBefore = sessions.SessionChildren.Sum(child =>
            child.ToolCallsReceived.Count(tool => tool == DebuggerResume));

        var refused = await CallAsync(rig, DebuggerResume, new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite exercising this call",
        });

        var text = TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue();

        // ⚠️ Answered like any tool this BrowserAI does not have, since
        // 2026-10-04 (previously "NOT in this server's tools/list" and the row's
        // own reason, "The reason is liveness"). The reason stays in the file.
        await Assert.That(text).IsEqualTo(SessionErrors.ToolDoesNotExist(DebuggerResume));

        // Nothing reached the child.
        await Assert.That(sessions.SessionChildren.Sum(child =>
            child.ToolCallsReceived.Count(tool => tool == DebuggerResume))).IsEqualTo(callsBefore);

        // And it is in the session's record, failed, carrying what the caller
        // was told.
        var row = RecordedSession.LogOf(directory).Single(entry => entry.Tool == DebuggerResume);

        await Assert.That(row.Outcome).IsEqualTo(SessionStore.Failed);
        await Assert.That(row.Failure).IsEqualTo(text);
    }

    // ⚠️ RETIRED 2026-09-21, and named here so the deletion is a record and
    // not an absence. `TheWebMcpCallIsWithheldOnLivenessAndTheWebMcpListIsNot`
    // stood between these two comments from 2026-09-15. It asserted that the
    // pair @playwright/mcp 0.0.81 added was judged in two directions -- the list
    // advertised and forwarded, the call dropped from the surface and refused at
    // the door -- and it opened by requiring BOTH names to be in the surface a
    // fully-capable child exposes, because an absence has to be this build's
    // decision and not a bundle that never carried the tool.
    //
    // @playwright/mcp 0.0.82 marked both `skillOnly`, so neither is on the wire
    // in any configuration, both verdict rows were deleted, and every premise
    // this arm rested on is gone. Deleting a test for a mechanism that no longer
    // exists is not a skip.
    //
    // WHAT IT WAS FOR IS PRESERVED , NOT LOST, because the tool can come
    // back and the judgement would then be owed again: the deny was LIVENESS
    // and not security -- upstream wraps the list path in a five-second
    // timeout and the call path in nothing, and a page whose handler never
    // settles held one call for 45,002 ms where a well-behaved tool on the same
    // page answered in 521 ms. That reasoning is in tool-verdicts.json's
    // `_rows_removed_because_upstream_withdrew_the_tool` and in
    // upstream-review.json, and it was RE-READ in the 0.0.82 bundle and is still
    // true of the code.
    //
    // ONE THING IT RECORDED IS NOW WORSE , NOT GONE. The arm's remarks
    // said a deny does not close the tab-header line `- N webmcp tools available
    // on the page`, and that the line "carries the count and none of the page's
    // text". At 0.0.82 the snapshot itself carries the page's tool names,
    // descriptions and inputSchemas, so page-authored text reaches a model on
    // every snapshot-bearing call. That is measured, not inferred -- kb:
    // "A page can add tools to the child's tools/list, and its own text reaches
    // a caller", re-verification row 133 -- and what would switch it off is a
    // `webmcp: false` this build does not write, which is a decision and not
    // a test.

    /// <summary>
    /// A <c>deny</c> row in <c>tool-verdicts.json</c> is dropped from the
    /// advertised list, refused at the door, and recorded -- for a tool this
    /// build does not actually deny.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The subject is the MECHANISM, and that is why it denies
    /// <c>browser_navigate</c> and not <c>browser_annotate</c>.</b> The arm
    /// above proves the shipped judgement; this one proves the judgement is
    /// <i>read from the file</i> -- which the shipped one cannot, because a
    /// hardcoded constant naming the same tool would satisfy every assertion
    /// about it. A rig copy of the file is what separates the two, and it leaves
    /// the product's own deny set untouched, which is what four documents publish
    /// a count of. *(Was "at exactly one" until 2026-09-15; it is two now, and
    /// the point is unchanged -- a rig copy must not move a published number.)*
    /// </para>
    /// <para>
    /// <b>Three claims, and each is the half the others do not cover.</b> Absent
    /// from the list, because a denied tool is dropped and not disabled --
    /// there is nothing for a model to read and weigh. Refused at the door,
    /// because a model that knows the name from upstream can still send it.
    /// Recorded, because <i>the agent reached for a tool this build will not
    /// forward</i> is a fact about the session and the session's record is now
    /// the only place it survives.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADenialInTheVerdictsFileIsUnadvertisedRefusedAtTheDoorAndRecorded()
    {
        // ⚠️ A name that is a SUBSTRING OF NO OTHER NAME, and that is not
        // fussiness: the whole-surface assertion below is a substring search, and
        // `browser_navigate` would fail it against `browser_navigate_back`
        // sitting legitimately in the list. Measured over the snapshot:
        // `browser_pdf_save` occurs exactly once in the whole tools array, which
        // is its own name.
        const string Denied = "browser_pdf_save";
        const string Why = "A rig copy of the file denies this one, so the refusal below is the file talking rather than a constant.";

        // The session child would answer it happily, so a proxy that forwarded
        // would visibly succeed here instead of failing for some other reason.
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools[Denied] = new FakeToolBehaviour(),
            verdicts: RepositoryVerdicts.Denying(Denied, Why));

        // The surface child answers with upstream's own committed list, so the
        // absence asserted below is a filter and not a double that never had
        // the tool.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult(),
            sessions: sessions);

        var advertised = await rig.Client.RoundTripAsync("tools/list", new JsonObject());

        var names = (advertised["tools"]?.AsArray() ?? [])
            .Select(tool => (string?)tool?["name"] ?? string.Empty)
            .ToList();

        // Not vacuous: the child really does advertise it.
        await Assert.That(rig.SurfaceChild.ToolsListResult).Contains(Denied);
        await Assert.That(names).DoesNotContain(Denied);

        // And nothing of it is left in the surface for a model to read: no
        // description mentioning it, no note saying it would refuse.
        await Assert.That(advertised.ToJsonString()).DoesNotContain(Denied);

        var before = RecordedSession.LogOf(rig.Session!).Count;
        var callsBefore = sessions.SessionChildren.Sum(child => child.ToolCallsReceived.Count(tool => tool == Denied));

        var refused = await CallAsync(rig, Denied, new JsonObject
        {
            ["session"] = rig.Session!,
            ["why"] = "reaching for a tool a rig copy of the verdicts file denies",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();

        // ⚠️ Since 2026-10-04 the answer is the one any tool this BrowserAI does
        // not have gets, and the file's `why` is no part of it: the row is read
        // from the file, which is what this arm proves, and the reason stays in
        // the file as the human record. Previously the file's own `why` was the
        // refusal, behind BrowserAI's own first sentence.
        await Assert.That(TextOf(refused)).IsEqualTo(SessionErrors.ToolDoesNotExist(Denied));
        await Assert.That(TextOf(refused)).DoesNotContain(Why);

        // Nothing reached the child.
        await Assert.That(sessions.SessionChildren.Sum(child =>
            child.ToolCallsReceived.Count(tool => tool == Denied))).IsEqualTo(callsBefore);

        // And it is in the record, failed and settled, carrying what the caller
        // was told and not a summary of it.
        var log = RecordedSession.LogOf(rig.Session!);
        var row = log.Single(entry => entry.Tool == Denied);

        await Assert.That(log.Count).IsEqualTo(before + 1);
        await Assert.That(row.Outcome).IsEqualTo(SessionStore.Failed);
        await Assert.That(row.SettledAt).IsNotNull();
        await Assert.That(row.Failure).IsEqualTo(TextOf(refused));
    }

    /// <summary>
    /// A tool with no verdict is refused at the door -- and, unlike a denied one,
    /// is still advertised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>DENY BY DEFAULT, and the asymmetry with a denial is deliberate
    /// and not an oversight.</b> A <c>deny</c> is a decision, so the tool is
    /// dropped from the list; a missing row is a <i>gap</i>, so the tool stays in
    /// the list and the call is refused. Two reasons. The gap is already loud --
    /// <c>ToolVerdictTests</c> is red on the same build -- so dropping it from the
    /// list would add nothing. And filtering on <i>absence</i> would turn a
    /// verdicts file that failed to load into a silently empty surface, which is
    /// the failure the loud loader exists to prevent.
    /// </para>
    /// <para>
    /// <b>Both shapes of gap, because they arrive differently.</b> A name in the
    /// child's own list that nobody judged is the Playwright bump; a name in
    /// nobody's list is a model reaching for a tool from another server, or a
    /// typo. Neither reaches the child, and neither is a browser upstream would
    /// have launched before telling us the tool does not exist.
    /// </para>
    /// <para>
    /// ⚠️ <b>And since 2026-10-04 the two are answered differently</b>, by the
    /// maintainer's decision of 2026-10-03: <i>"Calls to a tool BrowserAI doesn't
    /// have: a) Yes, in the same lane."</i> The first is a defect in the build and
    /// keeps a short refusal saying so; the second is told plainly that the tool
    /// does not exist in this BrowserAI.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AToolWithNoVerdictIsRefusedAtTheDoorAndStillAdvertised()
    {
        const string Unjudged = "browser_navigate";
        const string Nowhere = "browser_a_tool_from_the_future";

        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools[Unjudged] = new FakeToolBehaviour();
                child.Tools[Nowhere] = new FakeToolBehaviour();
            },
            verdicts: RepositoryVerdicts.Without(Unjudged));

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult(),
            sessions: sessions);

        var advertised = await rig.Client.RoundTripAsync("tools/list", new JsonObject());

        var names = (advertised["tools"]?.AsArray() ?? [])
            .Select(tool => (string?)tool?["name"] ?? string.Empty)
            .ToList();

        // Still advertised. This is the half a reader will not expect, so it is
        // asserted and not left to the remark above.
        await Assert.That(names).Contains(Unjudged);

        // ⚠️ TWO DIFFERENT ANSWERS SINCE 2026-10-04, because they are two
        // different faults. A name the list carries and the verdicts file does
        // not is a defect in the build; a name the list does not carry at all is
        // a tool this BrowserAI does not have, and the caller is told so plainly.
        foreach (var (tool, expected) in new[]
        {
            (Unjudged, SessionErrors.ToolHasNoVerdict()),
            (Nowhere, SessionErrors.ToolDoesNotExist(Nowhere)),
        })
        {
            var before = RecordedSession.LogOf(rig.Session!).Count;
            var callsBefore = sessions.SessionChildren.Sum(child => child.ToolCallsReceived.Count(received => received == tool));

            var refused = await CallAsync(rig, tool, new JsonObject
            {
                ["session"] = rig.Session!,
                ["why"] = "reaching for a tool this build has no verdict for",
            });

            await Assert.That((bool?)refused["isError"]).IsTrue();
            await Assert.That(TextOf(refused)).IsEqualTo(expected);

            // ⚠️ The listed tool's refusal quotes no name; the plain one names
            // the tool, in the maintainer's own wording of 2026-10-03 (Q371.6 c).
            if (tool == Unjudged)
            {
                await Assert.That(TextOf(refused)).DoesNotContain(tool);
            }
            else
            {
                await Assert.That(TextOf(refused)).Contains($"'{tool}'");
            }

            // Nothing reached the child -- which is the whole point: upstream
            // creates the browser context before it looks the name up.
            await Assert.That(sessions.SessionChildren.Sum(child =>
                child.ToolCallsReceived.Count(received => received == tool))).IsEqualTo(callsBefore);

            // And the record keeps the name VERBATIM, because "what did it try
            // to call" is exactly what a reader of the record wants and the
            // record is not model-facing.
            var log = RecordedSession.LogOf(rig.Session!);

            await Assert.That(log.Count).IsEqualTo(before + 1);
            await Assert.That(log[^1].Tool).IsEqualTo(tool);
            await Assert.That(log[^1].Outcome).IsEqualTo(SessionStore.Failed);
            await Assert.That(log[^1].Failure).IsEqualTo(TextOf(refused));
        }
    }

    /// <summary>
    /// A wrong JSON type on <c>name</c>, <c>session</c> or <c>why</c> is a named
    /// refusal, and never <c>-32603 Internal error</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>F6, and the whole of it is whose fault the answer says it is.</b>
    /// The three strings were read as
    /// <c>(node as JsonValue)?.GetValue&lt;string&gt;()</c>, and on
    /// <c>"name": 5</c> that does not answer <see langword="null"/> --
    /// <c>JsonValue</c> accepts a number and <c>GetValue&lt;string&gt;</c>
    /// throws <c>InvalidOperationException</c>, which escaped the whole handler
    /// and reached the caller as a bare <c>-32603</c> with the SDK's own
    /// wording. A model reading that is told BrowserAI broke; what happened is
    /// that it sent a number where the schema says string, which it can fix on
    /// the next turn if anybody tells it.
    /// </para>
    /// <para>
    /// <b>All three, because they are read on three different lines and a fix
    /// applied to one leaves the other two.</b> <c>name</c> is read before
    /// anything else and decides which handler runs at all; <c>session</c> and
    /// <c>why</c> are read out of the arguments object.
    /// </para>
    /// <para>
    /// <b>The refusal is asserted as a RESULT and not as an error frame</b>,
    /// because that is the distinction: a JSON-RPC error is a protocol failure
    /// and this is an answer that says no.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWrongJsonTypeOnNameSessionOrWhyIsANamedRefusalAndNeverAnInternalError()
    {
        // The positive control at the bottom has to reach a tool the child
        // actually answers, or "the same three arguments, right" proves only
        // that the double is a double.
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_snapshot"] = new FakeToolBehaviour());
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var wrongName = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = 5,
            ["arguments"] = new JsonObject { ["session"] = rig.Session!, ["why"] = "sending a number as a tool name" },
        });

        await Assert.That((bool?)wrongName["isError"]).IsTrue();
        await Assert.That(TextOf(wrongName)).Contains("'name' must be a string");
        await Assert.That(TextOf(wrongName)).Contains("Number");
        await Assert.That(TextOf(wrongName)).DoesNotContain("Internal error");

        var wrongSession = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_snapshot",
            ["arguments"] = new JsonObject { ["session"] = true, ["why"] = "sending a boolean as a session" },
        });

        await Assert.That((bool?)wrongSession["isError"]).IsTrue();
        await Assert.That(TextOf(wrongSession)).Contains("'session' must be a string");
        await Assert.That(TextOf(wrongSession)).Contains("True");

        var wrongWhy = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_snapshot",
            ["arguments"] = new JsonObject { ["session"] = rig.Session!, ["why"] = new JsonArray { "a", "b" } },
        });

        await Assert.That((bool?)wrongWhy["isError"]).IsTrue();
        await Assert.That(TextOf(wrongWhy)).Contains("'why' must be a string");
        await Assert.That(TextOf(wrongWhy)).Contains("Array");

        // ⚠️ THE POSITIVE CONTROL. The same three arguments, right this time,
        // reach the child -- so a version that refused everything would pass
        // every assertion above.
        var accepted = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_snapshot",
            ["arguments"] = new JsonObject { ["session"] = rig.Session!, ["why"] = "the same three arguments, right" },
        });

        await Assert.That((bool?)accepted["isError"]).IsNotEqualTo(true);
    }

    [Test]
    public async Task ACallNamingNoSessionIsRefusedAndReachesNoChildAtAll()
    {
        // ROUTING, and it is the one thing at this layer that is not negotiable.
        // A proxy holding N children cannot answer a call that names none of
        // them: before `session` was made mandatory such a call was answered by
        // the RUN'S OWN child, which is a session nobody chose the mode or the
        // directory of, and whose profile outlives the call.
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var callsBefore = sessions.SessionChildren.Sum(child => child.ToolCallsReceived.Count);

        var answer = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,<h1>ok</h1>",
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();
        await Assert.That(TextOf(answer)).IsEqualTo(SessionErrors.SessionMissing("browser_navigate"));

        // Neither a session child nor the run's own surface child was asked.
        await Assert.That(sessions.SessionChildren.Sum(child => child.ToolCallsReceived.Count)).IsEqualTo(callsBefore);
        await Assert.That(rig.SurfaceChild.ToolCallsReceived).DoesNotContain("browser_navigate");

        // And it is required in the advertised schema too, so a model is told
        // before it is refused and not after.
        var advertised = await rig.Client.RoundTripAsync("tools/list");

        var navigate = (advertised["tools"]?.AsArray() ?? [])
            .Single(tool => (string?)tool!["name"] == "browser_navigate")!
            .AsObject();

        await Assert.That(navigate["inputSchema"]?["required"]?.AsArray()
            .Select(entry => (string?)entry)
            .Contains(SessionToolSurface.SessionParameter)).IsTrue();
    }

    [Test]
    public async Task EveryCallReachesTheChildOfTheSessionItNamedUnderConcurrency()
    {
        // ⚠️ The failure this exists for is not a glitch: a call that resolved
        // one session's handle to another's child drives the WRONG BROWSER, and
        // it presents as nothing at all -- a successful call and a plausible
        // result, against a page the caller never asked for. So this drives real
        // calls across three sessions, all outstanding at the server together,
        // WHILE other sessions are being opened and destroyed on the same
        // connection, and checks which child each one landed in.
        //
        // Reframed again 2026-08-20 (previously the three sessions were one per
        // MODE, and the loop read SessionModes.All). Modes are gone; three
        // sessions is what the arithmetic below is written against and three is
        // what it keeps. The claim was never about modes -- it is about routing
        // a call to the child of the directory it named.
        //
        // Reframed 2026-08-18 (previously TheHandleToTypeLookupHoldsUnderConcurrencyAcrossModes,
        // which asserted that each answer matched the (tool, mode) verdict for
        // the handle that call named). With the permission matrix gone, a
        // verdict-based check would have been satisfied by "allowed" everywhere
        // and could no longer see a swapped lookup at all. What replaces it is
        // stronger, not weaker: the per-child call log says which session
        // actually received the work.
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directories = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in Concurrent)
        {
            var directory = Path.Combine(sessions.Root, $"concurrent-{name}");
            directories[name] = directory;

            var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = directory,
                ["purpose"] = $"the {name} session driven concurrently",
            });

            await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true);
        }

        // One probe per session per round, and the tool name carries which
        // session it was meant for -- so a call that landed in a neighbour's
        // child is visible in that child's own log and not inferred.
        const int Rounds = 25;

        var requests = new List<(string Method, JsonNode? Parameters)>();
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var round in Enumerable.Range(0, Rounds))
        {
            foreach (var name in Concurrent)
            {
                foreach (var tool in Probes)
                {
                    // The annotation probe is refused before it is routed, on
                    // every mode, so it is counted out of the expectation and
                    // not out of the batch: the call still goes over the wire,
                    // and a proxy that forwarded it anyway would show up as a
                    // surplus in that child's log.
                    if (RepositoryVerdicts.Committed.Decide(tool).IsAllowed)
                    {
                        expected[directories[name]] = expected.GetValueOrDefault(directories[name]) + 1;
                    }

                    // ⚠️ No `round` argument since 2026-10-04 (previously every
                    // probe carried one): an argument a schema does not have is
                    // refused since that day, and `browser_navigate` is in the list
                    // this rig's run-level child advertises. It gets the one argument
                    // its schema has instead; nothing ever read `round`.
                    var arguments = new JsonObject
                    {
                        ["session"] = directories[name],
                        ["why"] = "the suite exercising this call",
                    };

                    if (tool == "browser_navigate")
                    {
                        arguments["url"] = $"data:text/html,{round}";
                    }

                    requests.Add(("tools/call", new JsonObject
                    {
                        ["name"] = tool,
                        ["arguments"] = arguments,
                    }));
                }
            }

            // Churn, interleaved into the same batch and not run beside it:
            // the index the lookup reads is being written while the calls above
            // are being routed, which is the only state that could produce the
            // race.
            var churn = Path.Combine(sessions.Root, $"churn-{round}");

            requests.Add(("tools/call", new JsonObject
            {
                ["name"] = SessionToolSurface.Init,
                ["arguments"] = new JsonObject
                {
                    ["directory"] = churn,
                    ["purpose"] = "opened and destroyed while the probes run",
                },
            }));

            requests.Add(("tools/call", new JsonObject
            {
                ["name"] = SessionToolSurface.Destroy,
                ["arguments"] = new JsonObject { ["directory"] = churn, ["why"] = "the suite exercising this call" },
            }));
        }

        var answers = await rig.Client.RoundTripManyAsync(requests);

        await Assert.That(answers.Count).IsEqualTo(requests.Count);

        // Each double is paired with the session directory the product built its
        // launch options for, so the mapping is the product's and not a
        // guess about creation order.
        var children = sessions.SessionChildren;
        var launches = sessions.Launches;
        var wrong = new ConcurrentBag<string>();

        await Assert.That(children.Count).IsEqualTo(launches.Count);

        for (var index = 0; index < children.Count; index++)
        {
            // The child's working directory is the session's `output` folder,
            // which is how a bare relative filename lands inside the session.
            var session = Path.GetDirectoryName(launches[index].WorkingDirectory)!;
            var received = children[index].ToolCallsReceived.Where(Probes.Contains).ToList();

            if (received.Count != expected.GetValueOrDefault(session))
            {
                wrong.Add($"the child for '{session}' received {received.Count} probe calls, expected {expected.GetValueOrDefault(session)}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, wrong.Take(10))).IsEmpty();

        // The denominator, so a proxy that dropped calls could not pass by
        // making every count zero: 25 rounds × 3 sessions × 4 probes = 300
        // calls, minus the annotation probe, which every session refuses.
        //
        // Corrected 2026-08-18 (previously 25, counted as the rounds times the
        // modes that refused it -- one of three). The tool is withheld from the
        // surface and refused everywhere now, so it is 75, one per round per
        // session.
        var refusedByLiveness = RepositoryVerdicts.Committed.Decide(RepositoryVerdicts.ADenial.Name).IsAllowed
            ? 0
            : Rounds * Concurrent.Length;

        await Assert.That(expected.Values.Sum())
            .IsEqualTo((Rounds * Concurrent.Length * Probes.Length) - refusedByLiveness);

        await Assert.That(refusedByLiveness).IsEqualTo(75);

        // The batch really was concurrent and not a queue the client drained
        // one at a time: answers came back in a different order from the
        // requests. Asserted, not noted, because a serialising server
        // would make every claim above evidence about one call at a time.
        var firstId = answers.Min(answer => answer.Id);

        var outOfOrder = answers
            .Select((answer, position) => answer.Id - firstId != position)
            .Count(moved => moved);

        await Assert.That(outOfOrder).IsGreaterThan(0);
    }

    /// <summary>
    /// Four tools chosen so the probe set spans what the removal changed: the
    /// back door, a storage tool, the annotation tool and an ordinary one.
    /// </summary>
    private static readonly string[] Probes =
        ["browser_storage_state", "browser_run_code_unsafe", RepositoryVerdicts.ADenial.Name, "browser_navigate"];

    private static bool Allows(string tool) => RepositoryVerdicts.Committed.Decide(tool).IsAllowed;

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments)
    {
        var result = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

        return result;
    }

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// One table, six consumers, and the check that runs in both directions.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this file exists to catch has already happened once in this
/// project's history:</b> a fourth thing is added, three copies are updated, and
/// the fourth silently describes a system that no longer exists. Nothing breaks;
/// a model simply stops being told about a mode it should have picked, and the
/// symptom is an agent reporting a site as broken because it never logged in.
/// </para>
/// <para>
/// <b>So the assertion is bidirectional, and both directions were planted and
/// reverted on 2026-08-16.</b> Adding a fourth row to
/// <see cref="SessionModes.All"/> and nothing else turns
/// <see cref="EveryConsumerRendersEveryModeInTheTable"/> red, because the new
/// mode has no policy row and no expectation row; removing a mode from one
/// consumer — by hard-coding a rendering rather than deriving it — turns the same
/// test red from the other side.
/// </para>
/// </remarks>
internal sealed class ModelSurfaceTests
{
    /// <summary>
    /// Every authored tool's argument set and its required subset, written
    /// here rather than read from the class under test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two of these differ from §H.2's table, and the difference is
    /// deliberate rather than drift.</b> <c>browserai_resume</c> ships
    /// <c>tracing</c> and <c>consoleLevel</c>, which §H.2 gives only to
    /// <c>init</c>. The rule §H.2 states for refusing an argument on
    /// <c>resume</c> is that <i>"a profile is browser-specific"</i> — it is
    /// about <c>browser</c>, and about facts the directory on disk already
    /// records. Neither of these is such a fact: <c>tracing</c> becomes
    /// <c>saveSession</c> and <c>consoleLevel</c> becomes <c>console.level</c>
    /// in the config generated for <i>this run</i>, both of which may honestly
    /// differ from run to run without contradicting anything the session
    /// recorded. Refusing them would have meant destroying and recreating a
    /// session to turn tracing on for one afternoon.
    /// </para>
    /// <para>
    /// Recorded here because §H.2 is a plan section and this is what outlives
    /// it. The reason a signature table lives in the suite at all is that the
    /// arguments are the half of the model-facing surface nothing measured: the
    /// descriptions were budgeted in bytes and the mode table was rendered into
    /// four consumers, while the property names were whatever the class
    /// declared on the day.
    /// </para>
    /// </remarks>
    private static readonly (string Tool, string[] Properties, string[] Required)[] TheAuthoredSignatures =
    [
        (SessionToolSurface.Init,
            ["directory", "purpose", "mode", "browser", "tracing", "consoleLevel", "debug"],
            ["directory", "purpose", "mode"]),
        (SessionToolSurface.Resume,
            ["directory", "purpose", "debug", "tracing", "consoleLevel", "acknowledgeCopy"],
            ["directory"]),
        (SessionToolSurface.List, ["directory"], ["directory"]),
        (SessionToolSurface.Destroy, ["directory"], ["directory"]),
        (SessionToolSurface.SetPurpose, ["session", "purpose"], ["session", "purpose"]),
        (SessionToolSurface.ReinstallBrowser, [], []),
    ];

    /// <summary>
    /// The declared list of upstream phrases whose disappearance is a red build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a whole-string comparison</b> — the whole point of the rewrite is
    /// that the string changes. This is the record of <i>why</i> a sentence is
    /// there: a phrase is added the moment anyone decides upstream's wording is
    /// load-bearing, and a rewrite that drops it fails rather than quietly
    /// removing the warning a model was acting on.
    /// </para>
    /// <para>
    /// Every phrase below is one a model acts on — what a tool costs, what it
    /// refuses, that it is dangerous — read out of the committed snapshot on
    /// 2026-08-16 at <c>@playwright/mcp</c> 0.0.79.
    /// </para>
    /// </remarks>
    private static readonly (string Tool, string Phrase)[] LoadBearingUpstreamPhrases =
    [
        // The single most important sentence upstream ships: it is what tells a
        // model that this tool is not an ordinary one.
        ("browser_run_code_unsafe", "RCE-equivalent"),
        ("browser_run_code_unsafe", "executes arbitrary JavaScript in the Playwright server process"),

        // Blocks on a human. A model that does not know this schedules it and
        // waits forever.
        ("browser_annotate", "wait for the user to draw annotations"),

        // Names the tool whose output the argument comes from; without it the
        // number is unguessable.
        ("browser_network_request", "Use the number from browser_network_requests"),

        // Says the config is the RESOLVED one, which is the whole reason to call
        // it rather than to read the file.
        ("browser_get_config", "after merging CLI options, environment variables and config file"),

        // Where the credentials go, said in the description rather than only in
        // the schema.
        ("browser_storage_state", "cookies, local storage"),
    ];

    /// <summary>
    /// What <c>browserai_init</c>'s description must say, beyond what its
    /// arguments mean.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two requirements, from two documents, and neither was met until
    /// 2026-08-17.</b> The guidance design puts *"the real-Chrome-profile
    /// warning, and the retention policy"* in the creation tool's description —
    /// the spec requires retention to be stated <i>there</i>, and <c>init</c>'s
    /// description is the channel a model sees at the moment it reaches for the
    /// tool; the
    /// [DECISIONS](../../DECISIONS.md#shape-and-packaging) demands the same thing independently, because
    /// <c>init</c> accepts any path — *"the `init` tool description is a security
    /// surface … say plainly what pointing at an existing browser profile
    /// does"*. Retention was stated on <c>resume</c> and on <c>list</c>, which is
    /// everywhere except where it was required.
    /// </para>
    /// <para>
    /// <b>Phrases rather than a whole-string comparison</b>, for the same reason
    /// <see cref="LoadBearingUpstreamPhrases"/> is: the text will be reworded,
    /// and what must survive a rewording is the fact, not the sentence.
    /// </para>
    /// </remarks>
    private static readonly string[] RequiredInitPhrases =
    [
        // The security surface. A model that reads this and still points a
        // session at a real profile has been told; one that is not told has not.
        "real Chrome profile",
        "Any path is accepted and none is validated",
        "live cookies and logins",

        // The retention policy, stated where the session is created rather than
        // only where one is resumed or listed. The tool name is part of the
        // requirement: a retention policy with no way to act on it is a fact
        // rather than guidance.
        "nothing here expires",
        "never deletes a session directory",
        SessionToolSurface.Destroy,
    ];

    /// <summary>
    /// What each mode is expected to refuse, written down rather than computed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the sixth consumer of the one table, and it is a consumer
    /// precisely because it is written by hand.</b> Derived from the product's
    /// own decision it would agree with it by construction and could never fail;
    /// written down, a fourth mode is a red build with a number in the message.
    /// The counts are of the <b>59-tool union surface</b> BrowserAI advertises.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 to 58 / 59 / 59 (previously 41 / 41 / 58,
    /// measured 2026-08-16 against the five-class <c>(tool, mode)</c> permission
    /// matrix).</b> That matrix was removed: it was never a boundary against the
    /// caller, who chooses the session directory and can read the profile inside
    /// it as the same Windows user. The single tool any mode still refuses is
    /// <c>browser_annotate</c>, on a mode that promised no window, and the reason
    /// is <b>liveness</b> — it blocks until a human draws, and the window appears
    /// on a headless session too.
    /// </para>
    /// </remarks>
    private static readonly (string Mode, int Allowed, string[] Refused)[] Expected =
    [
        ("headless", 58, [SessionToolPolicy.AnnotateTool]),
        ("interactive", 59, []),
        ("persistent", 59, []),
    ];

    [Test]
    public async Task EveryConsumerRendersEveryModeInTheTable()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var missing = new List<string>();

        // Consumer 2 — init's description, read out of the advertised surface
        // rather than off the class, so what is checked is what a model receives.
        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var initDescription = (string?)advertised[SessionToolSurface.Init]?["description"] ?? string.Empty;

        // Consumer 4 — the refusal channel, triggered rather than quoted: a mode
        // argument that is not one of the three is answered with the table.
        var badMode = await TextOfAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "never-created"),
            ["purpose"] = "should never be created",
            ["mode"] = "headed",
        });

        foreach (var mode in SessionModes.All)
        {
            // 1. The server instructions.
            Require(missing, ServerInstructions.Text, mode.Name, "the server instructions");
            Require(missing, ServerInstructions.Text, mode.Grants, "the server instructions");

            // 2. init's description.
            Require(missing, initDescription, mode.Name, "browserai_init's description");
            Require(missing, initDescription, mode.Grants, "browserai_init's description");

            // 3. resume's result — one session per mode, opened and reopened.
            var resumed = await ResumeInModeAsync(rig, sessions, mode);
            Require(missing, resumed, mode.Name, "browserai_resume's result");
            Require(missing, resumed, mode.Grants, "browserai_resume's result");

            // 4. The refusal channel.
            Require(missing, badMode, mode.Name, "the refusal text");
            Require(missing, badMode, mode.Grants, "the refusal text");

            // 5. The generated child config, which is where a mode stops being
            //    a description and becomes two switches on a real browser.
            //
            //    ⚠️ Corrected 2026-08-18 (previously "session-type enforcement",
            //    asserting the mode had a row in the (tool, mode) permission
            //    matrix). That matrix is gone, and a check against it would now
            //    pass for every mode including one nobody had considered — a
            //    consumer that cannot fail is worse than a missing one. This is
            //    the consumer that was always doing the work: `Headed` becomes
            //    upstream's `headless`, and `Storage` becomes the capability set
            //    the session's own child is launched with, so a mode without it
            //    has no cookie tools IN ITS CHILD rather than a lookup declining
            //    to forward them.
            var generated = BrowserConfiguration.ForSession(
                SessionPath.Resolve(Path.Combine(sessions.Root, $"config-{mode.Name}")),
                mode,
                SessionManager.SupportedBrowser,
                tracing: false,
                BrowserConfiguration.DefaultConsoleLevel);

            var opinions = generated.Opinions.ToDictionary(
                opinion => opinion.Path,
                opinion => opinion.Value.ToJsonString(),
                StringComparer.Ordinal);

            var headless = mode.Headed ? "false" : "true";

            if (opinions.GetValueOrDefault("browser.launchOptions.headless") != headless)
            {
                missing.Add($"the generated config does not render '{mode.Name}' headed={mode.Headed}");
            }

            var capabilities = new JsonArray(
                [.. (mode.Storage ? BrowserConfiguration.UnionCapabilities : BrowserConfiguration.BaseCapabilities)
                    .Select(capability => (JsonNode)capability!)]).ToJsonString();

            if (opinions.GetValueOrDefault("capabilities") != capabilities)
            {
                missing.Add($"the generated config does not render '{mode.Name}' storage={mode.Storage}");
            }

            // 6. The tests. A mode nobody wrote an expectation for is a mode
            //    nobody checked, which reads as covered and is not.
            if (!Expected.Any(row => row.Mode == mode.Name))
            {
                missing.Add($"the tests carry no expected surface for '{mode.Name}'");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();
    }

    [Test]
    public async Task TheInstructionsStringFitsTheClientsSilentTruncationBudget()
    {
        // In BYTES rather than characters, and that is not pedantry: this string
        // carries '·' (2 bytes) and '—' (3 bytes), so a character count
        // under-reports precisely the string that uses them. The client cuts at
        // 2 KB with nothing reported, so anything past the cut has never been
        // read by anybody.
        await Assert.That(ServerInstructions.ByteCount).IsLessThanOrEqualTo(ServerInstructions.MaximumBytes);

        // Non-empty and actually wired: an instructions string the server never
        // sends is the same as not having one.
        await Assert.That(ServerInstructions.ByteCount).IsGreaterThan(400);

        // ⚠️ Re-measured 2026-08-18 off the published binary's own `initialize`
        // response: **1,261 characters and 1,276 bytes**, leaving 772. The three
        // mode lines cost 106, 121 and 92 bytes apiece.
        //
        // Corrected 2026-08-18 (previously "Measured 2026-08-16: 1,613
        // characters and **1,628 bytes** … The headroom is 420 bytes … Planting
        // a fourth mode measured its cost at 223 bytes, leaving 197"). The mode
        // lines used to carry what each mode REFUSES, rendered from the
        // (tool, mode) permission policy, and that policy was removed -- it was
        // never a boundary against the caller, who chooses the session directory
        // and reads the profile inside it as the same Windows user. The string
        // lost 352 bytes with it. The 223-byte figure is NOT carried forward: it
        // was measured against a line shape that no longer exists, and an
        // adjusted number is indistinguishable from a measured one.
        //
        // The headroom is still deliberately NOT a gate. A gate on it would fail
        // a fourth mode on a budget line instead of on the six-consumer line the
        // plant was aimed at, which is the wrong test failing; the hard cap above
        // is the requirement and it already catches running out.

        await using var rig = await McpTestHarness.ThroughTheProxyAsync();
        var initialize = await rig.Client.RoundTripAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = TestDefaults.CallerProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "budget-probe", ["version"] = "0" },
        });

        await Assert.That((string?)initialize["instructions"]).IsEqualTo(ServerInstructions.Text);
    }

    [Test]
    public async Task EveryToolDescriptionFitsTheSameBudget()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var oversized = new List<string>();

        foreach (var (name, tool) in advertised)
        {
            var description = (string?)tool?["description"] ?? string.Empty;
            var bytes = Encoding.UTF8.GetByteCount(description);

            if (bytes > SessionToolSurface.DescriptionMaximumBytes)
            {
                oversized.Add($"{name}: {bytes} bytes, over the {SessionToolSurface.DescriptionMaximumBytes} the client silently truncates at");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, oversized)).IsEmpty();

        // The whole surface, not a sample: 5 authored plus the 69 upstream can
        // ever expose. A count that had quietly shrunk would make the loop above
        // pass by measuring less.
        await Assert.That(advertised.Count).IsEqualTo(SessionToolSurface.Names.Count + 69);
    }

    [Test]
    public async Task TheCreationToolsDescriptionCarriesTheProfileWarningAndTheRetentionPolicy()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var description = (string?)Advertised(rig.SurfaceChild.ToolsListResult)[SessionToolSurface.Init]?["description"] ?? string.Empty;
        var missing = new List<string>();

        foreach (var required in RequiredInitPhrases)
        {
            if (!description.Contains(required, StringComparison.Ordinal))
            {
                missing.Add($"browserai_init's description no longer says '{required}'");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();

        // ⚠️ Re-measured 2026-08-18 off the published binary's own tools/list:
        // **1,639 bytes of 2,048, leaving 409.**
        //
        // Corrected 2026-08-18 (previously "Measured 2026-08-17, and the first
        // draft DID NOT FIT … the description now stands at 1,991 of 2,048 …
        // **57 bytes of headroom is the finding, and it is not a comfortable
        // number**"). It is comfortable now, and nothing was cut to make it so:
        // this description renders SessionModes.Table, whose clauses each carried
        // a second half naming what the mode REFUSES. That half came from the
        // (tool, mode) permission policy, which was removed, and the description
        // lost 352 bytes with it.
        //
        // The finding the old note carried still stands and is why the assertion
        // below exists: both required sentences are at the END of the string, so
        // an overflow deletes exactly the two things the charter demanded be
        // present, and the description grows without anybody editing this file
        // whenever the mode table does. Together with
        // EveryToolDescriptionFitsTheSameBudget that is a red build rather than a
        // warning nobody reads.
        var bytes = Encoding.UTF8.GetByteCount(description);

        await Assert.That(bytes).IsLessThanOrEqualTo(SessionToolSurface.DescriptionMaximumBytes);
    }

    [Test]
    public async Task EveryLoadBearingUpstreamPhraseSurvivesOurRewrite()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var lost = new List<string>();

        foreach (var (tool, phrase) in LoadBearingUpstreamPhrases)
        {
            var description = (string?)advertised[tool]?["description"];

            if (description is null)
            {
                lost.Add($"{tool}: not in the advertised surface at all, so its declared phrase cannot be checked");
                continue;
            }

            if (!description.Contains(phrase, StringComparison.Ordinal))
            {
                lost.Add($"{tool}: lost '{phrase}' — either our rewrite dropped it or upstream reworded it, and both are changes nobody adjudicated");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, lost)).IsEmpty();
    }

    [Test]
    public async Task OurAdditionIsAppendedAndNeverReplacesUpstreamsOwnText()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var upstream = UpstreamSurface.SnapshotDescriptions();
        var offenders = new List<string>();

        foreach (var (name, original) in upstream)
        {
            var rewritten = (string?)advertised[name]?["description"] ?? string.Empty;

            // Append-only, asserted as a prefix rather than as containment: an
            // insertion in the middle would still "contain" upstream's text
            // while changing where a model reads it.
            if (!rewritten.StartsWith(original, StringComparison.Ordinal))
            {
                offenders.Add($"{name}: upstream's description is no longer the start of ours");
            }

            var note = SessionToolPolicy.Note(name);

            if (note is null && rewritten.Length != original.Length)
            {
                offenders.Add($"{name}: every mode permits it, so nothing should have been appended");
            }

            if (note is not null && !rewritten.EndsWith(note, StringComparison.Ordinal))
            {
                offenders.Add($"{name}: the mode note is missing from the end of the description");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ Corrected 2026-08-18 (previously this asserted that
        // `browser_storage_state`'s description named 'persistent' and
        // 'headless', because the (tool, mode) permission matrix appended a
        // sentence to every restricted tool). There is one appended sentence
        // left in the whole surface, and this is it — so what is asserted now is
        // that it is the ONLY one, which the old form could not say.
        var annotated = (string?)advertised[SessionToolPolicy.AnnotateTool]?["description"] ?? string.Empty;

        await Assert.That(annotated).Contains("blocks until the run is killed");
        await Assert.That(annotated).Contains("'headless'");
        await Assert.That(annotated).Contains("'interactive' or 'persistent'");

        var appended = upstream
            .Where(entry => SessionToolPolicy.Note(entry.Name) is not null)
            .Select(entry => entry.Name)
            .ToList();

        await Assert.That(string.Join(", ", appended)).IsEqualTo(SessionToolPolicy.AnnotateTool);

        // And nothing of the removed matrix survives anywhere in the surface a
        // model reads. A positive control comes first, because a sweep that
        // matches nothing is indistinguishable from a genuine absence.
        var everyDescription = string.Concat(advertised.Values.Select(tool => (string?)tool?["description"] ?? string.Empty));

        await Assert.That(everyDescription).Contains("BrowserAI refuses this");

        foreach (var gone in (string[])["needs a session created in", "is not one this build has classified", "refuses every browser tool"])
        {
            await Assert.That(everyDescription).DoesNotContain(gone);
        }
    }

    [Test]
    public async Task NoConditionalCompilationReachesTheEnforcementPath()
    {
        // A property of the artifact rather than of the source: the decision a
        // released binary takes must be the decision the suite took. A `#if
        // DEBUG` here would make every test above evidence about a build nobody
        // ships.
        //
        // The five files are unchanged since 2026-08-18, when the (tool, mode)
        // permission matrix was removed from the first of them. What they carry
        // now is routing — `session` is mandatory and resolves to one child —
        // plus the single liveness refusal, and both deserve the same guarantee
        // for the same reason: a caller cannot tell from the outside which build
        // it is talking to.
        string[] enforcement =
        [
            "src/BrowserAI/Sessions/SessionToolPolicy.cs",
            "src/BrowserAI/Sessions/SessionMode.cs",
            "src/BrowserAI/Sessions/SessionErrors.cs",
            "src/BrowserAI/Proxy/BrowserProxy.cs",
            "src/BrowserAI/Proxy/ServerInstructions.cs",
        ];

        string[] forbidden = ["#if", "#else", "#elif", "[Conditional", "System.Diagnostics.Conditional"];
        var offenders = new List<string>();

        foreach (var relative in enforcement)
        {
            var file = new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, relative));

            if (!file.Exists)
            {
                offenders.Add($"{relative}: missing, so this scan no longer covers the path it names");
                continue;
            }

            // Comments stripped, because this file's own prose says "no #if, no
            // [Conditional]" -- and a scan that could not tell a rule from its
            // statement would fail on the sentence describing it.
            var code = await RepositoryLayout.ReadCodeAsync(file);

            offenders.AddRange(forbidden
                .Where(needle => code.Contains(needle, StringComparison.Ordinal))
                .Select(needle => $"{relative}: carries '{needle}' on the enforcement path"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task NoEnvironmentVariableOrLaunchSwitchReachesTheEnforcementPath()
    {
        // The other half, and the half nothing checked until 2026-08-17. `#if`
        // is the compile-time route to relaxing a refusal; this is the run-time
        // one, and it is the worse of the two, because a conditionally-compiled
        // check at least ships as one artifact. A variable read here means the
        // binary the suite proved and the binary a developer runs with
        // BROWSERAI_LET_ME_THROUGH=1 set are the same artifact taking different
        // decisions, and nothing about the second reads differently from the
        // first.
        //
        // It matters more rather than less now that only the liveness refusal is
        // left: an environment variable that turned `browser_annotate` back on
        // in a headless session would hang an overnight run, and the hang is the
        // thing this product exists not to do.
        //
        // The convenience this forbids is real and is answered elsewhere:
        // `debug` on init and resume raises the log level so a refusal can be
        // *seen*, and changes no decision. That is the supported way to find
        // out why a call was refused.
        string[] enforcement =
        [
            "src/BrowserAI/Sessions/SessionToolPolicy.cs",
            "src/BrowserAI/Sessions/SessionMode.cs",
            "src/BrowserAI/Sessions/SessionErrors.cs",
            "src/BrowserAI/Proxy/ServerInstructions.cs",
        ];

        string[] forbidden =
        [
            "Environment.GetEnvironmentVariable",
            "Environment.GetEnvironmentVariables",
            "Environment.GetCommandLineArgs",
            "AppContext.TryGetSwitch",
            "Debugger.IsAttached",
            "RuntimeFeature",
        ];

        var offenders = new List<string>();

        foreach (var relative in enforcement)
        {
            var file = new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, relative));

            if (!file.Exists)
            {
                offenders.Add($"{relative}: missing, so this scan no longer covers the path it names");
                continue;
            }

            var code = await RepositoryLayout.ReadCodeAsync(file);

            offenders.AddRange(forbidden
                .Where(needle => code.Contains(needle, StringComparison.Ordinal))
                .Select(needle => $"{relative}: reads '{needle}' on the enforcement path"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // BrowserProxy is deliberately outside the list above rather than
        // silently omitted from it. It is the enforcement *call site* and also
        // the process's own composition root, so it legitimately reads the
        // environment for things that are not the decision -- and a scan that
        // banned the read outright would either be red today or would train the
        // next person to move the decision somewhere the scan does not look.
        // What is asserted instead is that the decision it calls is the one in
        // SessionToolPolicy, which the four files above are closed against.
        var callSite = await RepositoryLayout.ReadCodeAsync(
            new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, "src/BrowserAI/Proxy/BrowserProxy.cs")));

        await Assert.That(callSite).Contains("SessionToolPolicy.Decide");
    }

    private static void Require(List<string> missing, string rendered, string expected, string consumer)
    {
        if (!rendered.Contains(expected, StringComparison.Ordinal))
        {
            missing.Add($"{consumer} does not render '{expected}'");
        }
    }

    /// <summary>The advertised surface, keyed by name, as a caller receives it.</summary>
    [Test]
    public async Task EveryAuthoredToolAdvertisesExactlyTheArgumentSetItIsSpecifiedWith()
    {
        // §H.2 gives each of the six a signature, and until 2026-08-17 nothing
        // asserted any of them. Descriptions were measured, the mode table was
        // rendered into four consumers and checked -- and the *arguments*, which
        // are the half a model actually fills in, were whatever the class
        // happened to declare. An argument silently dropped from `init` would
        // show as a call the model stopped making, not as a red build.
        //
        // Read out of the advertised surface rather than off the class, for the
        // same reason the description assertions are: the rewrite is what a
        // model receives, and it is the rewrite that could lose a property.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var wrong = new List<string>();

        foreach (var (tool, expectedProperties, expectedRequired) in TheAuthoredSignatures)
        {
            var schema = advertised[tool]?["inputSchema"]?.AsObject();

            if (schema is null)
            {
                wrong.Add($"{tool}: not advertised at all");
                continue;
            }

            var properties = (schema["properties"]?.AsObject() ?? [])
                .Select(property => property.Key)
                .Order(StringComparer.Ordinal)
                .ToList();

            var required = (schema["required"]?.AsArray() ?? [])
                .Select(entry => (string)entry!)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (!properties.SequenceEqual(expectedProperties.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                wrong.Add($"{tool}: arguments are [{string.Join(", ", properties)}], specified as [{string.Join(", ", expectedProperties.Order(StringComparer.Ordinal))}]");
            }

            if (!required.SequenceEqual(expectedRequired.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                wrong.Add($"{tool}: required are [{string.Join(", ", required)}], specified as [{string.Join(", ", expectedRequired.Order(StringComparer.Ordinal))}]");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, wrong)).IsEmpty();

        // And the table above covers all six, so a seventh authored tool cannot
        // arrive unasserted.
        await Assert.That(TheAuthoredSignatures.Select(signature => signature.Tool).Order(StringComparer.Ordinal))
            .IsEquivalentTo(SessionToolSurface.Names.Order(StringComparer.Ordinal).ToArray());
    }

    private static Dictionary<string, JsonObject?> Advertised(string childToolsList)
    {
        var rewritten = SessionToolSurface.Rewrite(JsonNode.Parse(childToolsList)!.AsObject());

        return (rewritten["tools"]?.AsArray() ?? [])
            .ToDictionary(tool => (string)tool!["name"]!, tool => tool?.AsObject(), StringComparer.Ordinal);
    }

    /// <summary>Opens a session in one mode, closes nothing, and resumes it.</summary>
    private static async Task<string> ResumeInModeAsync(
        McpTestHarness rig,
        RigSessionEnvironment sessions,
        SessionModeDefinition mode)
    {
        var directory = Path.Combine(sessions.Root, $"consumer-{mode.Name}");

        _ = await TextOfAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = $"the {mode.Name} session the mode-table test resumes",
            ["mode"] = mode.Name,
        });

        return await TextOfAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
        });
    }

    private static async Task<string> TextOfAsync(McpTestHarness rig, string tool, JsonObject arguments)
    {
        var result = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

        return string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
    }
}

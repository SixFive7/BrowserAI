// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Runtime;

namespace BrowserAI.Sessions;

/// <summary>
/// The tools BrowserAI authors itself, and the one parameter it adds to
/// everybody else's.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a hand-written upstream schema.</b> The scope boundary forbids
/// typing a <c>@playwright/mcp</c> tool definition into C# — every one of those
/// originates from the child's <c>tools/list</c> at runtime, and this file never
/// describes one. What it declares is BrowserAI's own six tools — the six in
/// <see cref="Names"/> — which no child
/// knows about, and the <c>session</c> property that is <b>injected into</b> the
/// child's raw schemas rather than replacing them.
/// </para>
/// <para>
/// <b>The injection happens on the <see cref="JsonNode"/>, never on a typed
/// schema.</b> A typed <c>Tool</c> round trip silently discards tool-level
/// members it does not know — it carries no <c>[JsonExtensionData]</c> — so a
/// vendor extension upstream adds tomorrow would vanish on the way through. It is
/// also order-stable: <c>session</c> is appended to <c>properties</c> and to
/// <c>required</c>, and the tool array itself is untouched, because the spec
/// SHOULDs deterministic tool ordering for prompt-cache hit rates.
/// </para>
/// <para>
/// <b>The authored tools come first and upstream's follow in upstream's own
/// order.</b> <c>init</c> is the call that has to happen before any other, and
/// the surface is the first thing a model reads.
/// </para>
/// <para>
/// <b>Exactly one upstream tool is dropped on the way through</b>, and the
/// charter allows it in as many words: <i>filter, re-describe, inject
/// <c>session</c></i> is in scope and renaming is not. Which one, and the
/// measurement behind it, is
/// <see cref="SessionToolPolicy.IsWithheldFromTheSurface"/>'s to say. What
/// belongs here is the shape: it is <b>dropped, not disabled</b> — no entry, no
/// description explaining that it will refuse, nothing for a model to read and
/// weigh. A tool that can never succeed still costs attention and description
/// budget for as long as it is in the list.
/// </para>
/// </remarks>
internal static class SessionToolSurface
{
    /// <summary>Creates a session in a directory that is not already one.</summary>
    public const string Init = "browserai_init";

    /// <summary>Takes over a directory that already is one.</summary>
    public const string Resume = "browserai_resume";

    /// <summary>Reports every session beneath a directory.</summary>
    public const string List = "browserai_list";

    /// <summary>Deletes a session directory.</summary>
    public const string Destroy = "browserai_destroy";

    /// <summary>Replaces a session's recorded purpose.</summary>
    public const string SetPurpose = "browserai_set_purpose";

    /// <summary>
    /// Deletes one shared browser tree and downloads it again. The one authored
    /// tool that is machine-scoped rather than session-scoped.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-08-19 (previously "It takes no arguments because
    /// there is nothing to name: the browser install is shared by every session
    /// on the host").</b> That was true of a build with one family and stopped
    /// being true the day <c>browserai_init</c> accepted
    /// <c>browser: "firefox"</c> — there are now two trees, and the caller's
    /// broken browser is one of them. It takes exactly one required argument,
    /// naming the family; the weighing of both alternatives is in
    /// <c>SessionManager.ReinstallBrowserAsync</c>'s remarks. Still no session
    /// argument and still no force flag: this one has no session at all, which
    /// is a different thing from a default.
    /// <para>
    /// ⚠️ <b>And the argument gained a third value, <c>shared</c>, on
    /// 2026-08-19</b> — see <see cref="ProvisionedBrowsers.Shared"/>. It is not a
    /// family: <c>ffmpeg</c> and <c>winldd</c> are downloaded by both families
    /// into the same root, each with its own <c>INSTALLATION_COMPLETE</c>, and no
    /// family reinstall touches them, so a corrupted <c>ffmpeg</c> — which the
    /// <c>video</c> artifact type needs — had no route to repair through this
    /// server at all. <b>Explicitly rejected:</b> having a family reinstall also
    /// verify and repair the shared components. That is the repair tool that can
    /// break something working, which is the whole reason the argument is
    /// required.
    /// </para>
    /// </remarks>
    public const string ReinstallBrowser = "browserai_reinstall_browser";

    /// <summary>
    /// The second parameter every session-scoped call gains: <b>why</b> the
    /// caller is making it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It rides the same path <see cref="SessionParameter"/> does</b> —
    /// injected into the <see cref="JsonNode"/> the child sent, appended to
    /// <c>properties</c> and to <c>required</c>, with everything upstream wrote
    /// left where it was. The golden snapshot is unaffected: it records what
    /// upstream <i>offers</i>, captured from the child before the rewrite.
    /// </para>
    /// <para>
    /// <b>It goes on calls that NAME a session and nowhere else.</b>
    /// <c>browserai_list</c> is directory-scoped and
    /// <c>browserai_reinstall_browser</c> is machine-scoped, so neither has a
    /// session record to write into; <c>browserai_init</c> is excluded for a
    /// different reason — see its own <c>purpose</c>.
    /// </para>
    /// </remarks>
    public const string WhyParameter = "why";

    /// <summary>The parameter every upstream tool gains.</summary>
    /// <remarks>
    /// Named <c>session</c> rather than <c>handle</c> because it is not one: it
    /// is the session directory, which a model can always reconstruct, and which
    /// survives being compacted out of a model's context in a way an opaque
    /// token does not.
    /// </remarks>
    public const string SessionParameter = "session";

    /// <summary>The prefix that marks a tool as one of ours.</summary>
    public const string Prefix = "browserai_";

    /// <summary>
    /// What the client silently truncates a tool description at, and therefore
    /// what every description in the surface must fit inside.
    /// </summary>
    /// <remarks>
    /// The same cap as the server <c>instructions</c>, and the same silence: the
    /// tail of a longer description does not exist and nothing reports it.
    /// Counted in <b>UTF-16 characters</b>. <i>Corrected 2026-08-18 (previously
    /// <c>DescriptionMaximumBytes</c>, over a UTF-8 byte count, "because these
    /// strings carry <c>—</c> and <c>'</c> and a character count would
    /// under-report the ones that use them").</i> That reasoning was sound as
    /// conservatism and wrong as fact: measured @ Claude Code 2.1.234, a 2,048-
    /// character description weighing 6,004 bytes arrives whole. See
    /// <see cref="Proxy.ClientTruncationBudget"/> for the measurement.
    /// </remarks>
    public const int DescriptionMaximumCharacters = Proxy.ClientTruncationBudget.Characters;

    /// <summary>
    /// What a <c>description</c> <b>inside</b> an <c>inputSchema</c> must fit
    /// inside.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A house limit, not a client limit</b> — see
    /// <see cref="Proxy.ClientTruncationBudget.ParameterDescriptionCharacters"/>:
    /// measured 2026-08-18, the client truncates these at nothing, 20,000
    /// characters included. It stays enforced because it floats with a client
    /// version this project does not control and because this is the surface this
    /// type is most exposed on: <see cref="SessionDescription"/> is injected into
    /// every upstream tool's schema, so one string lands fifty-eight times and
    /// the day a release does start cutting schemas, one edit becomes fifty-eight
    /// silent truncations. <i>Corrected 2026-08-18 (previously "fifty-nine",
    /// twice): <c>browser_annotate</c> is withheld from the surface, so it is no
    /// longer one of the schemas this lands in.</i>
    /// </remarks>
    public const int ParameterDescriptionMaximumCharacters = Proxy.ClientTruncationBudget.ParameterDescriptionCharacters;

    /// <summary>
    /// The ten tools that became reachable on 2026-08-20, when session modes
    /// were deleted and every capability was granted to every session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not one of these has ever been reachable in this product or in the
    /// predecessor it was written against.</b> BrowserAI generated
    /// <c>capabilities</c> as <c>config</c>, <c>vision</c>, <c>devtools</c> and —
    /// on a <c>persistent</c> session only — <c>storage</c>. Upstream's
    /// <c>network</c>, <c>pdf</c> and <c>testing</c> capabilities were never
    /// named, so the ten tools below did not exist in any session's child: a
    /// caller naming one reached upstream and upstream answered that it did not
    /// know the tool. They are listed here rather than derived, because a
    /// deliberate grant that nothing records reads as a side effect of deleting
    /// something else.
    /// </para>
    /// <para>
    /// <b><c>browser_run_code_unsafe</c> is deliberately NOT in this list.</b> It
    /// is <c>core</c> and always was — reachable in every session this product
    /// has ever opened, including every <c>headless</c> one — and a reader
    /// meeting the grant below should not be able to come away thinking it
    /// arrived with it. The measurement that made it interesting is in
    /// <see cref="Runtime.BrowserConfiguration"/>'s history and in
    /// <c>DECISIONS.md</c>.
    /// </para>
    /// <para>
    /// <b>The one that changes what a human sees is
    /// <c>browser_route</c>.</b> Response mocking can make a page lie to a person
    /// watching a headed window: the browser renders what the mock returned, the
    /// address bar says the real origin, and nothing in the window says a rule is
    /// in force. That warning is in the server <c>instructions</c>, which are
    /// BrowserAI's own string — <b>never</b> appended to
    /// <c>browser_route</c>'s description, because upstream descriptions pass
    /// through byte for byte and this file has no rewrite path left to do it
    /// with.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> NewlyGrantedTools { get; } =
    [
        "browser_route",
        "browser_route_list",
        "browser_unroute",
        "browser_network_state_set",
        "browser_pdf_save",
        "browser_generate_locator",
        "browser_verify_element_visible",
        "browser_verify_text_visible",
        "browser_verify_list_visible",
        "browser_verify_value",
    ];

    /// <summary>The six authored tools, in the order they are offered.</summary>
    public static IReadOnlyList<string> Names { get; } = [Init, Resume, List, Destroy, SetPurpose, ReinstallBrowser];

    /// <summary>Whether a tool name belongs to BrowserAI rather than to the child.</summary>
    /// <param name="name">The tool name from a <c>tools/call</c>.</param>
    /// <returns>Whether BrowserAI answers it itself.</returns>
    public static bool IsAuthored(string? name) =>
        name is not null && name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Rewrites the child's <c>tools/list</c> result: <c>session</c> onto every
    /// tool, and the five authored tools in front.
    /// </summary>
    /// <param name="result">The child's result, parsed but not re-serialised through any typed contract.</param>
    /// <returns>The result to answer the caller with.</returns>
    public static JsonObject Rewrite(JsonObject result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rewritten = new JsonArray();

        foreach (var tool in Authored())
        {
            // Cast to JsonNode deliberately: JsonArray.Add<T>(T) is annotated
            // RequiresDynamicCode and RequiresUnreferencedCode, so the generic
            // overload is the one AOT trap this codebase has actually met.
            rewritten.Add((JsonNode)tool);
        }

        if (result[ToolsMember] is JsonArray tools)
        {
            // Detached and re-added rather than copied: the nodes are the child's
            // own, so nothing about them is re-serialised except the property
            // this method adds.
            foreach (var tool in tools.ToList())
            {
                _ = tools.Remove(tool);

                if (tool is JsonObject definition)
                {
                    // Dropped before anything is done to it. The name is read
                    // from the child's own node, so a tool upstream renames
                    // stops being filtered rather than being filtered by a stale
                    // spelling -- and the golden snapshot is what says the
                    // rename happened.
                    if (SessionToolPolicy.IsWithheldFromTheSurface((definition[NameMember] as JsonValue)?.GetValue<string>()))
                    {
                        continue;
                    }

                    InjectSession(definition);
                }

                rewritten.Add(tool);
            }
        }

        result[ToolsMember] = rewritten;
        return result;
    }

    private const string ToolsMember = "tools";
    private const string SchemaMember = "inputSchema";
    private const string PropertiesMember = "properties";
    private const string RequiredMember = "required";

    private const string SessionDescription =
        "The session directory, exactly as browserai_init or browserai_resume returned it. "
        + "This is the session: BrowserAI has no default and will not guess one.";

    /// <summary>
    /// What <c>why</c> asks for, on an upstream browser tool.
    /// </summary>
    /// <remarks>
    /// <b>Why rather than what, and the description has to do that work.</b> The
    /// tool name already says what the call does, so a restatement of it is a
    /// sentence nobody can use — <i>"clicking the submit button"</i> beside
    /// <c>browser_click</c> is noise. What no one can reconstruct afterwards is
    /// the intent: what was being established, checked or ruled out. The
    /// examples are in the string because a model given only the instruction
    /// writes the restatement.
    /// </remarks>
    private const string WhyDescription =
        "Why you are making this call — not what it does; the tool name already says that. "
        + "One short clause: \"checking whether the login survived the redirect\" beats \"clicking the submit button\". "
        + "It goes in this session's log, which is what lets the next agent to open the directory — or you, tomorrow — read back what was being attempted rather than only which tools ran.";

    private const string NameMember = "name";

    // ⚠️ DELETED 2026-08-18: `AppendModeNote`, which appended
    // `SessionToolPolicy.Note(name)` -- a sentence naming the modes
    // `browser_annotate` worked in -- to the one upstream description this build
    // rewrote.
    //
    // It is dead because the tool it wrote on is no longer in the list. The
    // sentence existed so a model could choose correctly at `init`, hours before
    // the refusal would otherwise arrive; a tool that is never advertised needs
    // no such warning, and a hook that can never fire reads as a live rewrite
    // path to whoever meets it next. So EVERY upstream description now passes
    // through byte for byte, which is a stronger property than "append, never
    // rewrite" and is asserted as one by
    // ModelSurfaceTests.EveryUpstreamDescriptionArrivesUnchangedAndTheWithheldToolDoesNotArriveAtAll.
    // Restoring the tool means restoring this method with it --
    // SessionToolPolicy.IsWithheldFromTheSurface says what that would take.
    private static void InjectSession(JsonObject tool)
    {
        if (tool[SchemaMember] is not JsonObject schema)
        {
            // A tool whose schema is not an object is upstream's business, not
            // ours. Manufacturing one would be inventing a contract for a tool
            // nobody described.
            return;
        }

        if (schema[PropertiesMember] is not JsonObject properties)
        {
            properties = [];
            schema[PropertiesMember] = properties;
        }

        // Appended, so every property upstream declared keeps its position. A
        // rewrite that reordered would cost a prompt-cache miss per call with
        // nothing failing.
        properties[SessionParameter] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = SessionDescription,
        };

        properties[WhyParameter] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = WhyDescription,
        };

        if (schema[RequiredMember] is not JsonArray required)
        {
            required = [];
            schema[RequiredMember] = required;
        }

        Require(required, SessionParameter);
        Require(required, WhyParameter);
    }

    /// <summary>
    /// Appends one name to a schema's <c>required</c> array unless it is already
    /// there.
    /// </summary>
    /// <remarks>
    /// Appended rather than inserted, so upstream's own required names keep
    /// their order — the same reason the properties are appended.
    /// </remarks>
    /// <param name="required">The schema's <c>required</c> array.</param>
    /// <param name="name">The property to require.</param>
    private static void Require(JsonArray required, string name)
    {
        if (!required.Any(entry => entry is JsonValue value && value.TryGetValue(out string? existing) && existing == name))
        {
            required.Add((JsonNode)name);
        }
    }

    private static IEnumerable<JsonObject> Authored()
    {
        yield return Tool(
            Init,
            "Create a BrowserAI browser session in a directory that is not already one.",
            "Creates a browser session whose home is the directory you name. The directory IS the session: everything this session stores — its browser profile, its screenshots and downloads, its log — lives there, and you name it again on every browser call. "
            + $"There is no default directory and no fallback; an empty, relative or unusable path is refused rather than turned into one that happens to work. If the directory is already a session, this refuses and tells you to call {Resume} — being made to say so is the point. "
            + "Every capability this server can grant is granted to every session, so there is nothing to choose and nothing bound that a later call has to live with. "
            + "'headed' opens a window for THIS run only; 'tracing' records the session into the output directory; 'debug' raises this session's log level for its life. All three are per-run and none is recorded. "
            + "SECURITY: name a NEW directory. Only two kinds of path are refused — one on a network drive, and a second spelling of a directory the filesystem calls something else — and nothing else about it is validated: one that already holds a browser profile — the user's real Chrome profile, or a copy — becomes this session's, and a 'persistent' session then drives its live cookies and logins, as can any agent given the path. "
            + $"RETENTION: nothing here expires. BrowserAI never deletes a session directory, so it stays until you call {Destroy}; {List} shows what has accumulated, and its size.",
            new JsonObject
            {
                ["directory"] = Property("string", "Absolute path of the session directory, on a LOCAL drive and spelled the way the filesystem spells it. It is created if it does not exist. This is also the session's name, so make it say what the session is for — 'checkout-flow-bug' beats a timestamp. A network path or a mapped network drive is refused, because one unreachable share stalls every session sharing that directory; so is a second spelling of one directory — a \\\\?\\ prefix, a junction, a subst drive — because two spellings make two locks over one lock file. Both refusals name the spelling to use instead."),
                ["purpose"] = Property("string", "The session's STANDING description: one sentence saying what this directory is for, which browserai_list shows six weeks from now and which whoever resumes it reads first. It is also the first entry in this session's log — init takes no separate 'why', because the purpose IS why the session exists. Write it for a stranger: 'reproducing the checkout 500 on staging' beats 'testing'."),
                ["headed"] = Property("boolean", "Open a visible browser window for this run. Defaults to false. It is a property of THIS launch and is not recorded: the same session can be resumed headed tomorrow and headless the day after, and nothing on disk changes either way. Turn it on when a human is going to watch, sign in, or clear something the agent cannot. A window is not a security control and this server makes no claim that it is — every session gets every tool, headed or not."),
                ["browser"] = Enumerated($"The browser family, permanent for the directory's life — a profile belongs to the browser that made it, so this cannot be changed on resume. Defaults to '{SessionManager.DefaultBrowser}'. Each family is downloaded once per machine on first use ({string.Join(", ", ProvisionedBrowsers.Families.Select(family => $"{family} {BrowserProvisioner.DownloadSizeFor(family)}"))}), so naming the other one for the first time starts a download and the first browser call is refused until it lands.", ProvisionedBrowsers.Families),
                ["tracing"] = Property("boolean", "Record this session into its output directory. A property of this run rather than of the session; defaults to false."),
                ["consoleLevel"] = Enumerated($"Which console messages browser tools return. Defaults to '{BrowserConfiguration.DefaultConsoleLevel}', which silently drops debug messages.", BrowserConfiguration.ConsoleLevels),
                ["debug"] = Property("boolean", "Raise this session's own log level for its life. Per session, so turning it on for the one misbehaving does not drown the others. Defaults to false."),
            },
            ["directory", "purpose"]);

        yield return Tool(
            Resume,
            "Take over a directory that is already a BrowserAI session.",
            "Reopens a session that exists, and replays what it was: its recorded browser, purpose and history. 'browser' is NOT an argument — it was bound when the session was created and a profile on disk belongs to its browser — and passing it is refused. "
            + "A session is resumable forever; there is no expiry, so a directory that exists can always be resumed. "
            + "This never refuses a directory for what its session has BEEN — only for how the path is written: a network path and a second spelling of one directory are refused here exactly as they are at init. If the session was moved or renamed, its record is repaired and you are told. If it is a COPY of a session that still exists somewhere else, it resumes and tells you that too — every field of the record is an ordered list of timestamped statements, so the answer shows you where the directory has been and that the recorded purpose describes the original. Read that before acting on the purpose, and set a new one.",
            new JsonObject
            {
                ["directory"] = Property("string", "Absolute path of an existing session directory, on a LOCAL drive and spelled the way the filesystem spells it — the same two refusals as init, each naming the spelling to use instead."),
                ["purpose"] = Property("string", "Optional, and NOT the same thing as 'why'. This is the session's STANDING description — what the directory is for, shown by browserai_list six weeks from now. Given here it is APPENDED to the recorded purpose rather than replacing it, so the directory keeps saying what it has been for. Leave it out unless what the session is FOR has changed; if you only want to say why you are opening it now, that is 'why'."),
                [WhyParameter] = Property("string", "Required, and NOT the same thing as 'purpose'. This is DISPOSABLE: why you are taking this session over at this moment, one short clause — \"picking up the checkout bug after the overnight run stopped\". It becomes one entry in this session's log, in order, beside the browser calls that follow it; it does not change what the session says it is for and nothing shows it in a listing."),
                ["headed"] = Property("boolean", "Open a visible browser window for this run. Defaults to false, and it is NOT read back from what the session was last time — every run says what it wants. Accepted here as well as on init because the case that matters is a session created headless that now needs a human to sign in."),
                ["debug"] = Property("boolean", "Raise this session's own log level for its life. Accepted here as well as on init, because the interesting case is almost always a session that is already running badly. Defaults to false."),
                ["tracing"] = Property("boolean", "Record this run of the session into its output directory. Defaults to false."),
                ["consoleLevel"] = Enumerated($"Which console messages browser tools return. Defaults to '{BrowserConfiguration.DefaultConsoleLevel}'.", BrowserConfiguration.ConsoleLevels),
            },
            ["directory", WhyParameter]);

        yield return Tool(
            List,
            "List every BrowserAI session beneath a directory.",
            "Reports every session under the path you name: its browser, recorded purpose, when it was created and last used, and its size on disk — because retention is your decision and you cannot make it well without knowing you are sitting on four gigabytes. "
            + "There is no unscoped form: breadth is stated rather than assumed. Pass a drive root to see everything, and the size of the answer is then your own doing. The directory need not exist; a path with nothing under it returns an empty list, which is an answer rather than an error.",
            new JsonObject
            {
                ["directory"] = Property("string", "Absolute path of the tree to look under. A drive root lists everything on that volume."),
            },
            ["directory"]);

        yield return Tool(
            Destroy,
            "Delete a BrowserAI session directory and everything in it.",
            "Closes the session's browser and deletes the whole directory — profile, output, downloads and log. "
            + "It REFUSES any directory that does not hold a valid BrowserAI session record, which is what makes it safe: it cannot be aimed at Documents. "
            + "If another process holds the session, this reports who instead of waiting. If a file is held open, the rest is still deleted and the report names what survived.",
            new JsonObject
            {
                ["directory"] = Property("string", "Absolute path of the session directory to delete."),
                [WhyParameter] = Property("string", "Why this session is being deleted — not what destroy does. One short clause: \"the bug is reproduced and written up\", or \"this was a copy nobody needed\". It is the last entry in a log that is about to be deleted with everything else, so it is written for the answer this call returns rather than for a later reader."),
            },
            ["directory", WhyParameter]);

        yield return Tool(
            SetPurpose,
            "Replace a session's recorded purpose.",
            "Rewrites what the session says it is for. The previous purpose is kept in the session's history rather than lost, and is returned to you. Works on a session that is running and on one that is not.",
            new JsonObject
            {
                [SessionParameter] = Property("string", "Absolute path of the session directory."),
                ["purpose"] = Property("string", "The session's new STANDING description — what this directory is for from now on. It REPLACES the current one as the answer to 'what is this session', it is what browserai_list shows six weeks from now, and it is what whoever resumes the directory reads first. Write it as a description of the work, not of this moment: \"tracking the checkout redirect loop on staging\". The previous purpose is kept in the session's history rather than lost."),
                [WhyParameter] = Property("string", "Why you are changing it RIGHT NOW — this one is disposable. It is not a second draft of the purpose and it is not shown in any listing: it becomes one dated entry in this session's log, sitting between the browser calls that led to the change, so a later reader can see what made the purpose move. \"the original login bug turned out to be a redirect loop\" is a 'why'; \"tracking the checkout redirect loop on staging\" is a 'purpose'. If what you are about to write would still be true next week, it belongs in 'purpose'."),
            },
            [SessionParameter, "purpose", WhyParameter]);

        yield return Tool(
            ReinstallBrowser,
            "Delete one of BrowserAI's shared browser installs and download it again.",
            "Removes the tree BrowserAI provisioned for the thing you name and downloads the exact revision this build pins, into BrowserAI's own directory. "
            + $"'browser' is REQUIRED and there is no default: BrowserAI provisions {string.Join(" and ", ProvisionedBrowsers.Families)} into separate trees, and a default would delete and re-download a healthy one while the broken one stayed broken and the answer said it had been reinstalled. "
            + $"'{ProvisionedBrowsers.Shared}' is not a browser: it is {string.Join(" and ", ProvisionedBrowsers.SharedComponents)}, which BOTH families download into the same place and neither family's reinstall touches. Name it when video recording fails or a browser reports a missing dependency — those two are what the 'video' artifact type needs, and until this value existed a corrupted one could not be repaired through this server at all. "
            + "It REFUSES while a browser of the family you named is running, and names the sessions that are open instead. "
            + $"'{ProvisionedBrowsers.Shared}' is stricter and refuses while ANY session is open, of EITHER family: both families use these components, and a browser only starts the codec at the moment it records, so 'nothing is using it right now' is not the same statement as 'nothing is about to'. "
            + "There is deliberately no force option, because forcing here means terminating browsers other agents are driving. "
            + "Use it for something that is installed and broken: an install that completed once is never re-downloaded on its own, because the marker written at the end short-circuits every later check without validating anything, so a single quarantined or corrupted file stays broken forever. "
            + "For a browser that was simply never downloaded, call browserai_init instead — it starts the download and returns immediately. This one waits for the download to finish, so it takes as long as the download does.",
            new JsonObject
            {
                ["browser"] = Enumerated(
                    $"What to delete and download again. The trees are independent, so name the one that is broken — the others are untouched. "
                    + $"Download sizes: {string.Join(", ", ProvisionedBrowsers.Families.Select(family => $"{family} {BrowserProvisioner.DownloadSizeFor(family)}"))}. "
                    + $"'{ProvisionedBrowsers.Shared}' is {string.Join(" and ", ProvisionedBrowsers.SharedComponents)} together, a few megabytes, and is the only way to repair them.",
                    ProvisionedBrowsers.ReinstallTargets),
            },
            ["browser"]);
    }

    private static JsonObject Tool(string name, string title, string description, JsonObject properties, string[] required)
    {
        var requiredNames = new JsonArray();

        foreach (var entry in required)
        {
            requiredNames.Add((JsonNode)entry);
        }

        return new JsonObject
        {
            ["name"] = name,
            ["title"] = title,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = requiredNames,
            },
        };
    }

    private static JsonObject Property(string type, string description) =>
        new() { ["type"] = type, ["description"] = description };

    private static JsonObject Enumerated(string description, IReadOnlyList<string> values)
    {
        var allowed = new JsonArray();

        foreach (var value in values)
        {
            allowed.Add((JsonNode)value);
        }

        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = allowed,
        };
    }
}

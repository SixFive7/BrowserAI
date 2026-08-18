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
/// describes one. What it declares is BrowserAI's own five tools, which no child
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
    /// Deletes the shared browser tree and downloads it again. The one authored
    /// tool that is machine-scoped rather than session-scoped.
    /// </summary>
    /// <remarks>
    /// It takes no arguments <b>because there is nothing to name</b>: the browser
    /// install is shared by every session on the host, which is exactly why it
    /// refuses to act while any of them has a browser open. Every other authored
    /// tool names its session explicitly and none has an implicit scope; this one
    /// has no session at all, which is a different thing from a default.
    /// </remarks>
    public const string ReinstallBrowser = "browserai_reinstall_browser";

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
    /// Measured in <b>bytes</b>, because these strings carry <c>—</c> and <c>'</c>
    /// and a character count would under-report the ones that use them.
    /// Documented for this surface: see <see cref="Proxy.ClientTruncationBudget"/>
    /// for the sentence it is quoted from and for what that sentence leaves open.
    /// </remarks>
    public const int DescriptionMaximumBytes = Proxy.ClientTruncationBudget.Bytes;

    /// <summary>
    /// What a <c>description</c> <b>inside</b> an <c>inputSchema</c> must fit
    /// inside.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Assumed rather than documented</b> — see
    /// <see cref="Proxy.ClientTruncationBudget.ParameterDescriptionBytes"/>. It is
    /// the surface this type is most exposed on: <see cref="SessionDescription"/>
    /// is injected into every upstream tool's schema, so one string lands
    /// fifty-nine times and an overflow would be fifty-nine silent truncations
    /// from one edit.
    /// </remarks>
    public const int ParameterDescriptionMaximumBytes = Proxy.ClientTruncationBudget.ParameterDescriptionBytes;

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
                    InjectSession(definition);
                    AppendModeNote(definition);
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

    private const string DescriptionMember = "description";
    private const string NameMember = "name";

    /// <summary>
    /// Appends BrowserAI's own sentence to the one tool this build refuses on
    /// some modes, and leaves every other description exactly as upstream wrote
    /// it.
    /// </summary>
    /// <remarks>
    /// <b>Append, never rewrite.</b> Upstream's descriptions carry text a model
    /// acts on — what a tool refuses, what it costs, which argument is
    /// destructive — and a phrase lost in a rewrite fails silently: the tool
    /// still works and the model is simply no longer warned. So ours goes on the
    /// end, upstream's is untouched, and a declared list of upstream phrases is
    /// asserted to survive.
    /// </remarks>
    private static void AppendModeNote(JsonObject tool)
    {
        if ((tool[NameMember] as JsonValue)?.GetValue<string>() is not { } name)
        {
            return;
        }

        if (SessionToolPolicy.Note(name) is not { } note)
        {
            return;
        }

        var upstream = (tool[DescriptionMember] as JsonValue)?.GetValue<string>();

        tool[DescriptionMember] = upstream is null or ""
            ? note
            : $"{upstream}\n\n{note}";
    }

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

        if (schema[RequiredMember] is not JsonArray required)
        {
            required = [];
            schema[RequiredMember] = required;
        }

        if (!required.Any(entry => entry is JsonValue value && value.TryGetValue(out string? name) && name == SessionParameter))
        {
            required.Add((JsonNode)SessionParameter);
        }
    }

    private static IEnumerable<JsonObject> Authored()
    {
        yield return Tool(
            Init,
            "Create a BrowserAI browser session in a directory that is not already one.",
            "Creates a browser session whose home is the directory you name. The directory IS the session: everything this session stores — its browser profile, its screenshots and downloads, its log — lives there, and you name it again on every browser call. "
            + $"There is no default directory and no fallback; an empty, relative or unusable path is refused rather than turned into one that happens to work. If the directory is already a session, this refuses and tells you to call {Resume} — being made to say so is the point. "
            + $"The mode is permanent for the directory's life and is required, because a mode chosen by omission is a security posture nobody decided on: {SessionModes.Table} "
            + "'tracing' is a boolean orthogonal to all three and records the session into the output directory. 'debug' raises this session's log level only, for its life, and changes nothing else. "
            + "SECURITY: name a NEW directory. Any path is accepted and none is validated: one that already holds a browser profile — the user's real Chrome profile, or a copy — becomes this session's, and a 'persistent' session then drives its live cookies and logins, as can any agent given the path. "
            + $"RETENTION: nothing here expires. BrowserAI never deletes a session directory, so it stays until you call {Destroy}; {List} shows what has accumulated, and its size.",
            new JsonObject
            {
                ["directory"] = Property("string", "Absolute path of the session directory. It is created if it does not exist. This is also the session's name, so make it say what the session is for — 'checkout-flow-bug' beats a timestamp."),
                ["purpose"] = Property("string", "One sentence saying what this session is for. It is recorded, replayed to whoever resumes the directory later, and is the only thing that makes an old session identifiable."),
                ["mode"] = Enumerated("Permanent for the life of the directory. " + SessionModes.Table, [.. SessionModes.All.Select(mode => mode.Name)]),
                ["browser"] = Enumerated($"The browser family. Defaults to '{SessionManager.SupportedBrowser}', which is the only one this build supports.", [SessionManager.SupportedBrowser]),
                ["tracing"] = Property("boolean", "Record this session into its output directory. Orthogonal to the mode; defaults to false."),
                ["consoleLevel"] = Enumerated($"Which console messages browser tools return. Defaults to '{BrowserConfiguration.DefaultConsoleLevel}', which silently drops debug messages.", BrowserConfiguration.ConsoleLevels),
                ["debug"] = Property("boolean", "Raise this session's own log level for its life. Per session, so turning it on for the one misbehaving does not drown the others. Defaults to false."),
            },
            ["directory", "purpose", "mode"]);

        yield return Tool(
            Resume,
            "Take over a directory that is already a BrowserAI session.",
            "Reopens a session that exists, and replays what it was: its recorded mode, browser, purpose and history. Mode and browser are NOT arguments — they were bound when the session was created and a profile on disk belongs to its browser — and passing either is refused. "
            + "A session is resumable forever; there is no expiry, so a directory that exists can always be resumed. "
            + "If the directory was moved or renamed, its record is repaired and you are told. If it looks like a COPY of a session that still exists somewhere else, this refuses, because a copy carries another session's history and ownership record; pass acknowledgeCopy=true to take it over deliberately.",
            new JsonObject
            {
                ["directory"] = Property("string", "Absolute path of an existing session directory."),
                ["purpose"] = Property("string", "Optional. Appended to the recorded purpose rather than replacing it, so the directory keeps saying what it has been for."),
                ["debug"] = Property("boolean", "Raise this session's own log level for its life. Accepted here as well as on init, because the interesting case is almost always a session that is already running badly. Defaults to false."),
                ["tracing"] = Property("boolean", "Record this run of the session into its output directory. Defaults to false."),
                ["consoleLevel"] = Enumerated($"Which console messages browser tools return. Defaults to '{BrowserConfiguration.DefaultConsoleLevel}'.", BrowserConfiguration.ConsoleLevels),
                ["acknowledgeCopy"] = Property("boolean", "Take over a directory that appears to be a copy of a session that still exists. Defaults to false, which refuses. A directory that was MOVED needs no acknowledgement — a move is repaired without asking."),
            },
            ["directory"]);

        yield return Tool(
            List,
            "List every BrowserAI session beneath a directory.",
            "Reports every session under the path you name: its mode, browser, recorded purpose, when it was created and last used, and its size on disk — because retention is your decision and you cannot make it well without knowing you are sitting on four gigabytes. "
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
            },
            ["directory"]);

        yield return Tool(
            SetPurpose,
            "Replace a session's recorded purpose.",
            "Rewrites what the session says it is for. The previous purpose is kept in the session's history rather than lost, and is returned to you. Works on a session that is running and on one that is not.",
            new JsonObject
            {
                [SessionParameter] = Property("string", "Absolute path of the session directory."),
                ["purpose"] = Property("string", "The new one-sentence purpose."),
            },
            [SessionParameter, "purpose"]);

        yield return Tool(
            ReinstallBrowser,
            "Delete BrowserAI's shared browser install and download it again.",
            "Removes the browser tree BrowserAI provisioned and downloads the exact revision this build pins, into BrowserAI's own directory. It takes no arguments because there is nothing to name: the install is shared by every session on this machine. "
            + "It REFUSES while any session anywhere has a browser open, and names what is running instead — there is deliberately no force option, because forcing here means terminating browsers other agents are driving, and Windows will not delete a directory whose executables are open in any case. "
            + "Use it for a browser that is installed and broken: an install that completed once is never re-downloaded on its own, because the marker written at the end short-circuits every later check without validating anything, so a single quarantined or corrupted file stays broken forever. "
            + "For a browser that was simply never downloaded, call browserai_init instead — it starts the download and returns immediately. This one waits for the download to finish, so it takes as long as the download does.",
            [],
            []);
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

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserAI.Sessions;

/// <summary>Where a call naming a tool is answered, if it is answered at all.</summary>
internal enum ToolVerdictKind
{
    /// <summary>Forwarded to the child of the session it names, byte-identical.</summary>
    Allow,

    /// <summary>
    /// Refused at BrowserAI's door and kept out of <c>tools/list</c> entirely.
    /// </summary>
    Deny,

    /// <summary>BrowserAI answers it itself; it never had a child to reach.</summary>
    Answer,
}

/// <summary>One tool, and what this build does with a call naming it.</summary>
/// <param name="Name">The tool name, exactly as the shipped file spells it.</param>
/// <param name="Kind">Where the call is answered.</param>
/// <param name="Why">
/// On a <see cref="ToolVerdictKind.Deny"/>, the reason the tool is denied. ⚠️
/// <i>Corrected 2026-10-04 (previously "the reason a caller reads -- the whole of
/// the refusal below BrowserAI's own first sentence")</i>: a denied tool is
/// answered like any tool this BrowserAI does not have since that day, so the
/// reason is the human record and no model reads it. <see langword="null"/>
/// otherwise; a <c>deny</c> without one does not load.
/// </param>
/// <param name="Since">
/// On a <see cref="ToolVerdictKind.Deny"/>, the ISO date the judgement was made.
/// It is provenance for a person reading the file and is deliberately absent
/// from the refusal, which is written for a model deciding what to do next.
/// </param>
/// <param name="Note">
/// On an <see cref="ToolVerdictKind.Allow"/>, a short note BrowserAI appends to
/// the tool's own description, after <see cref="SessionToolSurface.NoteMarker"/>;
/// <see langword="null"/> when there is none. Added 2026-10-04 with the
/// maintainer's rewrite b of the instructions, which moved the advice for one
/// tool onto that tool.
/// </param>
/// <param name="Refusals">
/// On an <see cref="ToolVerdictKind.Allow"/>, the calls of the tool BrowserAI refuses in
/// a session whose own settings make them unsafe, each an argument and a per-run switch;
/// <see langword="null"/> when there is none. Added 2026-11-18 with the maintainer's
/// 208 b, see <see cref="ArgumentRefusal"/>.
/// </param>
internal sealed record ToolVerdict(
    string Name,
    ToolVerdictKind Kind,
    string? Why,
    string? Since,
    string? Note = null,
    IReadOnlyList<ArgumentRefusal>? Refusals = null)
{
    /// <summary>The refusal that answers one call of this tool, or <see langword="null"/> when the call may pass.</summary>
    /// <param name="arguments">The caller's arguments, as they arrived.</param>
    /// <param name="isOn">Whether a per-run switch is on in the session the call named.</param>
    /// <returns>The first refusal whose argument and switch the call meets.</returns>
    public ArgumentRefusal? RefusalFor(JsonObject? arguments, Func<string, bool> isOn) =>
        Refusals?.FirstOrDefault(refusal => refusal.Applies(arguments, isOn));
}

/// <summary>
/// One argument of an allowed tool that BrowserAI refuses while one of the session's own
/// per-run switches is on.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 208 b, 2026-11-18, in his words verbatim:</b> <i>"208 a + b (until
/// the upstream fix comes in)"</i>. The first rule BrowserAI keeps about a call's arguments
/// and not its tool, and it is data beside the verdict, as a note is: the row says which
/// argument, which switch, the words a caller reads, why and until when, and the code that
/// applies it names no tool. The one row is <c>browser_tabs</c>' <c>isolatedContext</c> in a
/// session with <c>captureNetwork</c> on, because Playwright writes every context's HTTP
/// Archive to the one path the session gave it and the default context's write, made last,
/// erases what an isolated context recorded. The row goes when a Playwright release fixes
/// that, which <c>TODO.md</c> watches.
/// </para>
/// <para>
/// <b>An argument names something when its value is a string that is neither empty nor one
/// of <see cref="Exempt"/></b>, which are upstream's own spellings of the case that is not
/// the hazard: <c>"default"</c> is the default context, and so is an empty or absent
/// name. A value of another type is upstream's to refuse, by its own schema.
/// </para>
/// </remarks>
/// <param name="Argument">The argument, as the tool's own schema spells it.</param>
/// <param name="Exempt">Values of it that do not turn the refusal on: the JSON member <c>except</c>.</param>
/// <param name="Setting">The per-run switch that must be on, one of <see cref="SessionRunSettings.Switches"/>: the JSON member <c>while</c>.</param>
/// <param name="Refusal">What a caller reads after BrowserAI's own first sentence: why, and what to do instead.</param>
/// <param name="Why">The record of the judgement.</param>
/// <param name="Since">The ISO date it was judged.</param>
/// <param name="Until">What ends it.</param>
internal sealed record ArgumentRefusal(
    string Argument,
    IReadOnlyList<string> Exempt,
    string Setting,
    string Refusal,
    string Why,
    string Since,
    string Until)
{
    /// <summary>Whether a call with these arguments, in a session with these switches, is refused.</summary>
    /// <param name="arguments">The caller's arguments.</param>
    /// <param name="isOn">Whether a per-run switch is on.</param>
    /// <returns>Whether the refusal applies.</returns>
    public bool Applies(JsonObject? arguments, Func<string, bool> isOn)
    {
        ArgumentNullException.ThrowIfNull(isOn);

        return arguments?[Argument] is JsonValue value
            && value.TryGetValue<string>(out var named)
            && named.Length > 0
            && !Exempt.Contains(named, StringComparer.Ordinal)
            && isOn(Setting);
    }
}

/// <summary>
/// Every tool BrowserAI knows of, read from the <c>tool-verdicts.json</c> compiled
/// into the binary beside the tool list it judges.
/// </summary>
/// <remarks>
/// <para>
/// <b>The list is data because the decision is ours and the tool set is
/// upstream's.</b> Until 2026-08-26 the whole of this was one C# constant --
/// <c>SessionToolPolicy.AnnotateTool</c>, a denylist of exactly one name, with a
/// refusal written out longhand in <see cref="SessionErrors"/> beside it. That
/// works for one tool and answers nothing about the other sixty-eight: a build
/// could not say <i>whether anybody had judged</i> a name, only whether it was
/// the one name it had heard of, so a tool upstream added arrived on this
/// server's surface and was forwarded before any human saw it.
/// </para>
/// <para>
/// <b>DENY BY DEFAULT, and the window that opens is bounded by a red build
/// and not by a promise.</b> A name with no row is refused at the door --
/// <see cref="Decide"/> -- because the alternative is that the file stops being a
/// gate and becomes an inventory. What makes that safe and not a slow
/// capability leak is <c>ToolVerdictTests</c>: every tool in the golden
/// <c>tools-list.json</c> snapshot must have a row here and every row must name a
/// tool the snapshot carries, checked in both directions on every run. A
/// Playwright bump that adds a tool therefore reddens the suite in the same pass
/// that reddens the snapshot diff, and the two are adjudicated together.
/// </para>
/// <para>
/// ⚠️ <b>This reverses a decision taken 2026-08-18, and it reverses it for a
/// reason that decision's own reasoning does not reach.</b> A <c>(tool, mode)</c>
/// deny-by-default matrix was deleted that day as <i>security theatre</i> -- it
/// was never a boundary against a caller who owns the session directory and reads
/// the profile inside it as the same user, and that argument is untouched and
/// still correct. What is different here is that a verdict is not a permission:
/// it decides <b>whether a name this build has never been told about is worth
/// starting a browser for</b>. Upstream looks a tool name up <i>after</i>
/// creating the browser context (<c>coreBundle.js</c>: the CLI factory's
/// <c>create</c> runs at <c>:73101</c>, the name lookup at <c>:65533</c>), so a
/// call naming nothing launches a browser to be told there is nothing to run --
/// and upstream's answer echoes the caller's own string back into model-facing
/// text. Neither of those is a permission question and neither was in scope on
/// 2026-08-18.
/// </para>
/// <para>
/// <b>A missing or malformed file is a loud failure and never an empty
/// allowlist.</b> Under deny-by-default an empty set denies everything, so a
/// silent fallback would present as <i>every tool suddenly refused</i> with
/// nothing anywhere naming the file. Every refusal below names the file and what
/// was wrong with it.
/// </para>
/// <para>
/// ⚠️ <b>Compiled into the binary since 2026-10-08</b>, with the tool list it
/// judges (<see cref="UpstreamToolList"/>). <i>Corrected (previously "the
/// <c>tool-verdicts.json</c> that ships beside the payload it describes", read from
/// the payload at startup through <c>Read</c>, and "<c>PayloadLayout.Verify</c>
/// reports an absent one as an incomplete payload before a child is ever
/// started")</i>. A file that is absent is a build that fails naming it, and one
/// this code cannot read fails the first read of <see cref="Compiled"/>, at
/// startup, with the loader's own sentence.
/// </para>
/// </remarks>
internal sealed class ToolVerdicts
{
    /// <summary>The file the build compiles in, at the repository root.</summary>
    public const string FileName = "tool-verdicts.json";

    /// <summary>The name the build gives the file it compiles into the binary.</summary>
    public const string ResourceName = "BrowserAI.tool-verdicts.json";

    private static readonly Lazy<ToolVerdicts> CompiledVerdicts = new(FromResource);

    /// <summary>The verdicts this binary was built with.</summary>
    /// <remarks>
    /// <b>A <see cref="Lazy{T}"/> and not a static initializer</b>, so that a file
    /// this build cannot read fails the first read with the loader's own sentence
    /// naming the resource, and not with a type initializer's.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The build embedded no verdicts, or ones this code cannot read.</exception>
    public static ToolVerdicts Compiled => CompiledVerdicts.Value;

    /// <summary>
    /// The only schema this build can read.
    /// </summary>
    /// <remarks>
    /// Checked and not ignored: a shape this code does not understand should be
    /// said instead of read by the half it recognises. ⚠️ <i>Corrected 2026-10-08
    /// (previously "the file travels inside the payload, which an update replaces
    /// wholesale, so a binary meeting a shape it does not understand should say
    /// so")</i>: the file is compiled in, so the meeting happens at the first read
    /// of a build that changed one without the other.
    /// </remarks>
    public const int SchemaVersion = 1;

    private const string UpstreamMember = "upstream";
    private const string AuthoredMember = "authored";
    private const string VerdictMember = "verdict";
    private const string WhyMember = "why";
    private const string SinceMember = "since";
    private const string NoteMember = "note";
    private const string RefuseMember = "refuse";
    private const string ArgumentMember = "argument";
    private const string ExceptMember = "except";
    private const string WhileMember = "while";
    private const string RefusalMember = "refusal";
    private const string UntilMember = "until";
    private const string SchemaVersionMember = "schemaVersion";

    /// <summary>Every member an argument refusal has, in the order the file writes them.</summary>
    private static readonly string[] RefusalMembers = [ArgumentMember, ExceptMember, WhileMember, RefusalMember, WhyMember, SinceMember, UntilMember];
    private const string JudgedAgainstMember = "judgedAgainst";

    private readonly FrozenDictionary<string, ToolVerdict> _rows;

    private ToolVerdicts(
        string origin,
        IReadOnlyList<ToolVerdict> upstream,
        IReadOnlyList<ToolVerdict> authored,
        IReadOnlyDictionary<string, string> judgedAgainst)
    {
        Origin = origin;
        Upstream = upstream;
        Authored = authored;
        JudgedAgainst = judgedAgainst;

        _rows = upstream.Concat(authored).ToFrozenDictionary(row => row.Name, StringComparer.Ordinal);
    }

    /// <summary>Where this was read from, so every refusal can name it.</summary>
    public string Origin { get; }

    /// <summary>Upstream's tools, in the file's own order.</summary>
    public IReadOnlyList<ToolVerdict> Upstream { get; }

    /// <summary>BrowserAI's own tools, in the file's own order.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-26, later the same day (previously "These rows
    /// have no run-time effect at all ... `SessionToolSurface.IsAuthored`
    /// short-circuits every `browserai_` name ... before `Decide` is reached, so
    /// removing an `answer` row changes nothing a caller can observe. Their role
    /// is build-and-test-time").*** <b>The short-circuit is an exact match
    /// now</b>, so a <c>browserai_</c> name with no row here reaches
    /// <see cref="Decide"/> and is deny-by-defaulted like any other unjudged
    /// name. Removing an <c>answer</c> row is therefore observable: that tool
    /// stops being answered and starts being refused at the door.
    /// </para>
    /// <para>
    /// <b>The build-time role is still the load-bearing one, and it is what
    /// keeps the two in step.</b>
    /// <c>ToolVerdictTests.TheAuthoredRowsAreExactlyTheToolsBrowserAiAnswersItself</c>
    /// holds these rows identical to <c>SessionToolSurface.Names</c> in both
    /// directions -- which is why a row cannot be removed by accident -- and
    /// <c>build/Write-ReleaseManifest.ps1</c> copies the whole file beside the
    /// release so a rollback can read which tools a build forwarded and which
    /// upstream that judgement was made against. <c>SessionToolSurface.Rewrite</c>
    /// still advertises the authored tools from <c>Names</c> and not from the
    /// file; the <see cref="Upstream"/> half has always decided every call.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ToolVerdict> Authored { get; }

    /// <summary>
    /// The package versions the judgements were made against.
    /// </summary>
    /// <remarks>
    /// Nothing at run time compares this with the child that is actually running:
    /// the file is compiled into the binary that is packed with the payload it
    /// describes, so the two move together or the install is broken, which is what
    /// each session's check on its child's tool list says
    /// (<see cref="UpstreamToolList"/>). What compares it is <c>ToolVerdictTests</c>,
    /// against the committed payload lock, which is where a disagreement is still
    /// fixable. <i>Corrected 2026-10-08 (previously "the file travels inside the
    /// payload it describes").</i>
    /// </remarks>
    public IReadOnlyDictionary<string, string> JudgedAgainst { get; }

    // ⚠️ DELETED 2026-10-08: `Read(string path)`, which read the payload's copy of
    // the file at startup and refused a missing one as an incomplete payload. The
    // file is compiled into the binary since that day, `Compiled` below, and
    // nothing reads it from disk.

    /// <summary>Reads the copy the build compiled into the binary.</summary>
    /// <returns>The verdicts.</returns>
    /// <exception cref="InvalidOperationException">There is no such resource, or it cannot be read.</exception>
    private static ToolVerdicts FromResource()
    {
        // Copied out of the image's memory, never read through a stream: see
        // EmbeddedResource (2026-10-11, previously Stream.CopyTo into a MemoryStream).
        var bytes = Hosting.EmbeddedResource.BytesOf(typeof(ToolVerdicts).Assembly, ResourceName)
            ?? throw new InvalidOperationException(
                $"This BrowserAI was built without its tool verdicts: the resource '{ResourceName}' is not in the binary, so it cannot tell which tools it may forward.");

        return Parse(bytes, ResourceName);
    }

    /// <summary>Reads the file from bytes somebody else supplied.</summary>
    /// <remarks>
    /// <b>The seam exists so a rig can vary the file without writing one</b>, and
    /// so the malformed-file arms can plant every shape this method refuses.
    /// <paramref name="origin"/> is carried into every message because a refusal
    /// that does not name the file it read is a refusal nobody can act on.
    /// </remarks>
    /// <param name="json">The file's bytes.</param>
    /// <param name="origin">What to call it in a failure message.</param>
    /// <returns>The verdicts.</returns>
    /// <exception cref="InvalidOperationException">The bytes are not a verdicts file this build can read.</exception>
    public static ToolVerdicts Parse(ReadOnlySpan<byte> json, string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json.ToArray());
        }
        catch (JsonException malformed)
        {
            throw new InvalidOperationException(
                $"'{origin}' is not readable JSON, so BrowserAI cannot tell which tools it is allowed to forward: {malformed.Message}",
                malformed);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object)
            {
                throw Unreadable(origin, $"its root is {root.ValueKind}, not an object");
            }

            if (!root.TryGetProperty(SchemaVersionMember, out var schema)
                || schema.ValueKind is not JsonValueKind.Number
                || !schema.TryGetInt32(out var version)
                || version != SchemaVersion)
            {
                throw Unreadable(
                    origin,
                    $"its '{SchemaVersionMember}' is not {SchemaVersion}, which is the only shape this build of BrowserAI can read");
            }

            var upstream = Rows(root, origin, UpstreamMember, authored: false);
            var authored = Rows(root, origin, AuthoredMember, authored: true);

            if (upstream.Count is 0)
            {
                throw Unreadable(origin, $"its '{UpstreamMember}' names no tool at all, and a verdicts file with no rows refuses every call");
            }

            var duplicates = upstream.Select(row => row.Name)
                .Intersect(authored.Select(row => row.Name), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (duplicates.Count > 0)
            {
                throw Unreadable(
                    origin,
                    $"'{string.Join("', '", duplicates)}' appears in both '{UpstreamMember}' and '{AuthoredMember}', so one name carries two verdicts");
            }

            return new ToolVerdicts(origin, upstream, authored, Judgements(root, origin));
        }
    }

    /// <summary>The verdict for one tool, or <see langword="null"/> when nobody judged it.</summary>
    /// <param name="tool">The name a caller sent, or one from the child's own list.</param>
    /// <returns>The row, or <see langword="null"/>.</returns>
    public ToolVerdict? Find(string? tool) =>
        tool is not null && _rows.TryGetValue(tool, out var verdict) ? verdict : null;

    /// <summary>
    /// Whether a tool the child advertises is kept out of the list BrowserAI
    /// advertises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A <c>deny</c> row and nothing else, and the asymmetry with
    /// <see cref="Decide"/> is deliberate.</b> A denied tool is <b>dropped, not
    /// disabled</b> -- no entry, no description explaining that it will refuse,
    /// nothing for a model to read and weigh -- because a tool that can never
    /// succeed costs attention and description budget for as long as it is in the
    /// list. A tool with <i>no</i> row is a different thing: it is a gap and
    /// not a decision, so it is still advertised and refused at the door. Two
    /// reasons, and the second is the stronger. A gap is already loud -- the
    /// coverage comparison is red on the same build -- so the advertisement adds
    /// nothing to it. And filtering on <i>absence</i> would make a file that
    /// failed to load present as an empty surface, which is the silent failure the
    /// loud loader above exists to prevent.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool name, as the child spells it.</param>
    /// <returns>Whether BrowserAI keeps it out of the surface.</returns>
    public bool IsWithheldFromTheSurface(string? tool) =>
        Find(tool) is { Kind: ToolVerdictKind.Deny };

    /// <summary>Decides one call to a tool the child would otherwise be asked for.</summary>
    /// <remarks>
    /// <para>
    /// <b>Two refusals, because a caller can act on the difference.</b> A
    /// <c>deny</c> answers with the file's own <c>why</c> behind BrowserAI's own
    /// first sentence -- <i>this build was told not to forward it, here is what to
    /// do instead</i>. A name with no row answers that this build has no verdict
    /// at all -- <i>this is a gap, call <c>tools/list</c></i>. Collapsing them
    /// would send a model looking for a permission to acquire in the one case and
    /// for a typo in the other.
    /// </para>
    /// <para>
    /// <b>An <c>answer</c> row cannot reach here and is refused and not
    /// allowed if it ever does.</b> <c>SessionToolSurface.IsAuthored</c>
    /// short-circuits the seven authored names before the door, and the loader
    /// refuses an <c>answer</c> row whose name is not one of them -- so the branch
    /// is unreachable by construction, and the direction it fails in is the one
    /// that does not forward an authored name to a child that has never heard of
    /// it.
    /// </para>
    /// <para>
    /// ⚠️ <b>An unjudged name in the authored NAMESPACE does reach here, and did
    /// not until 2026-08-26.</b> The short-circuit was a prefix test, so
    /// <c>browserai_zzz</c> never arrived: it was refused by the session
    /// manager's default arm and, having resolved no session, wrote no log row,
    /// while an unjudged <i>upstream</i> name was recorded. It is an exact match
    /// now, so both are deny-by-defaulted here and both are recorded against the
    /// session they named.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool the caller named.</param>
    /// <returns>Permission, or a refusal a model can act on in one turn.</returns>
    public ToolDecision Decide(string? tool) =>
        Find(tool) switch
        {
            { Kind: ToolVerdictKind.Allow } => ToolDecision.Allowed,
            // ⚠️ A DENIED TOOL IS ANSWERED LIKE A TOOL THIS BROWSERAI DOES NOT
            // HAVE, since 2026-10-04 (previously with SessionErrors.ToolIsDenied,
            // the row's own `why` behind "is deliberately NOT in this server's
            // tools/list"): a tool BrowserAI does not offer should look to a model
            // like any other it does not have. The `why` stays in the file as the
            // human record.
            //
            // ⚠️ AND THE WORDS ARE THE PROXY'S, since 2026-10-10, round 2 of the
            // texts review, #49 (previously
            // "ToolDecision.Refused(SessionErrors.ToolDoesNotExist(denied.Name))",
            // which said "listed below" with no list after it): BrowserProxy answers
            // a denied name at its door, with the tool list, before it asks anything
            // here, and only the suite reached this arm.
            { Kind: ToolVerdictKind.Deny } => ToolDecision.Withheld,
            _ => ToolDecision.Refused(SessionErrors.ToolHasNoVerdict()),
        };

    private static Dictionary<string, string> Judgements(JsonElement root, string origin)
    {
        if (!root.TryGetProperty(JudgedAgainstMember, out var judged) || judged.ValueKind is not JsonValueKind.Object)
        {
            throw Unreadable(origin, $"it carries no '{JudgedAgainstMember}' object saying which upstream the judgements were made against");
        }

        var versions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var package in judged.EnumerateObject())
        {
            if (package.Value.ValueKind is not JsonValueKind.String || package.Value.GetString() is not { Length: > 0 } resolved)
            {
                throw Unreadable(origin, $"'{JudgedAgainstMember}.{package.Name}' is not a version string");
            }

            versions[package.Name] = resolved;
        }

        return versions.Count is 0
            ? throw Unreadable(origin, $"'{JudgedAgainstMember}' names no package, so nothing says which upstream was judged")
            : versions;
    }

    private static List<ToolVerdict> Rows(JsonElement root, string origin, string member, bool authored)
    {
        if (!root.TryGetProperty(member, out var rows) || rows.ValueKind is not JsonValueKind.Object)
        {
            throw Unreadable(origin, $"it carries no '{member}' object");
        }

        var verdicts = new List<ToolVerdict>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows.EnumerateObject())
        {
            // ⚠️ THE SAME NAME TWICE INSIDE ONE HALF, which the both-halves
            // check below cannot see and which JSON does not forbid. Measured
            // 2026-08-26 on .NET 10: `JsonDocument` keeps both properties, so
            // this loop produced two rows and the constructor's
            // `ToFrozenDictionary` threw a bare `ArgumentException` -- reaching
            // `Program`'s process boundary and exiting 1 with a message naming
            // NEITHER the file NOR the row, against this type's own promise that
            // every refusal names both. The quieter half is worse:
            // `TryGetProperty` answers the LAST duplicate, so a doctored file
            // could carry `allow` and `deny` for one tool and a reader that got
            // past this point would pick one silently.
            if (!seen.Add(row.Name))
            {
                throw Unreadable(
                    origin,
                    $"'{row.Name}' appears twice in '{member}', so one name carries two verdicts and a reader would take whichever was written second");
            }

            verdicts.Add(Row(origin, member, row, authored));
        }

        return verdicts;
    }

    private static ToolVerdict Row(string origin, string member, JsonProperty row, bool authored)
    {
        if (row.Value.ValueKind is not JsonValueKind.Object)
        {
            throw Unreadable(origin, $"'{member}.{row.Name}' is {row.Value.ValueKind}, not an object");
        }

        if (!row.Value.TryGetProperty(VerdictMember, out var word) || word.ValueKind is not JsonValueKind.String)
        {
            throw Unreadable(origin, $"'{member}.{row.Name}' carries no '{VerdictMember}'");
        }

        var kind = word.GetString() switch
        {
            "allow" => ToolVerdictKind.Allow,
            "deny" => ToolVerdictKind.Deny,
            "answer" => ToolVerdictKind.Answer,
            var other => throw Unreadable(
                origin,
                $"'{member}.{row.Name}' carries the verdict '{other}', and the only verdicts are 'allow', 'deny' and 'answer'"),
        };

        // The two halves of the file are two different questions, and a row in
        // the wrong half is a statement nobody meant to make: an upstream tool
        // marked `answer` claims BrowserAI implements it, and an authored tool
        // marked `allow` claims the door decides whether it runs.
        //
        // ⚠️ *Corrected 2026-09-21 (previously "an authored tool marked `allow`
        // claims a child could run it").* One of them reaches a child now --
        // `browserai_page_tool` forwards a `tools/call` to the session's own --
        // so the thing `allow` would wrongly claim is no longer "a child" but
        // "this file decides". It does not: `BrowserProxy` skips the door for
        // every authored name, because the list that advertises them is the
        // list that judges them.
        if (authored && kind is not ToolVerdictKind.Answer)
        {
            throw Unreadable(
                origin,
                $"'{member}.{row.Name}' is one of BrowserAI's own tools and carries the verdict '{word.GetString()}'. Authored tools are always 'answer': the door does not judge them, because being in SessionToolSurface.Names IS the judgement");
        }

        if (authored && !SessionToolSurface.IsAuthored(row.Name))
        {
            // Since 2026-08-26 this is an EXACT match and not the prefix
            // test it used to be, so the message names the set and not the
            // spelling: a `browserai_` name that is not one of the seven is a row
            // about a tool that does not exist, which used to load.
            throw Unreadable(
                origin,
                $"'{member}.{row.Name}' is in '{AuthoredMember}' and is not one of BrowserAI's own tools -- those are exactly: {string.Join(", ", SessionToolSurface.Names)}");
        }

        if (!authored && kind is ToolVerdictKind.Answer)
        {
            throw Unreadable(
                origin,
                $"'{member}.{row.Name}' carries 'answer', which says BrowserAI implements it. Upstream's tools are 'allow' or 'deny'");
        }

        var why = Optional(row.Value, WhyMember);
        var since = Optional(row.Value, SinceMember);
        var note = Note(origin, member, row, kind);
        var refusals = Refusals(origin, member, row, kind);

        if (kind is not ToolVerdictKind.Deny)
        {
            return new ToolVerdict(row.Name, kind, why, since, note, refusals);
        }

        // A denial with no reason is the shape this whole file exists to
        // replace: the C# constant it grew out of carried its reasoning in a
        // doc comment nobody downstream could read, and the refusal a caller met
        // was written out longhand somewhere else entirely.
        if (why is not { Length: > 0 })
        {
            throw Unreadable(
                origin,
                $"'{member}.{row.Name}' is denied and carries no '{WhyMember}'. The '{WhyMember}' IS the refusal the caller reads, so a denial without one refuses with nothing to act on");
        }

        return since is { Length: > 0 }
            ? new ToolVerdict(row.Name, kind, why, since)
            : throw Unreadable(origin, $"'{member}.{row.Name}' is denied and carries no '{SinceMember}' saying when that was judged");
    }

    /// <summary>
    /// A row's BrowserAI note, or <see langword="null"/> when it has none.
    /// </summary>
    /// <remarks>
    /// <b>A non-empty string on an <c>allow</c> row, and nowhere else.</b> The
    /// note is appended to the description of a tool BrowserAI forwards; on a
    /// denied tool it would describe a tool nobody is shown, and on one of
    /// BrowserAI's own it would be a second description of a tool whose
    /// description is already BrowserAI's. Added 2026-10-04 with the maintainer's
    /// rewrite b of the server instructions.
    /// </remarks>
    /// <param name="origin">What to call the file in a failure message.</param>
    /// <param name="member">Which half the row is in.</param>
    /// <param name="row">The row.</param>
    /// <param name="kind">Its verdict.</param>
    /// <returns>The note, or <see langword="null"/>.</returns>
    private static string? Note(string origin, string member, JsonProperty row, ToolVerdictKind kind)
    {
        if (!row.Value.TryGetProperty(NoteMember, out var note))
        {
            return null;
        }

        if (note.ValueKind is not JsonValueKind.String || note.GetString() is not { Length: > 0 } text)
        {
            throw Unreadable(origin, $"'{member}.{row.Name}.{NoteMember}' is not a non-empty string, and a note is text BrowserAI appends to the tool's description");
        }

        return kind is ToolVerdictKind.Allow
            ? text
            : throw Unreadable(origin, $"'{member}.{row.Name}' carries a '{NoteMember}' and its verdict is not 'allow'. A note is appended to the description of a tool BrowserAI forwards, and only an allowed tool has one a model is shown");
    }

    /// <summary>
    /// A row's argument refusals, or <see langword="null"/> when it has none.
    /// </summary>
    /// <remarks>
    /// <b>On an <c>allow</c> row only, and every member required but <c>except</c></b>,
    /// because a refusal narrows a tool BrowserAI forwards and is a judgement somebody
    /// has to be able to re-examine and end: its words, its reason, its date and what
    /// ends it. A member this build does not know is refused by name, so a misspelt
    /// <c>while</c> cannot load as a refusal that is never on. Added 2026-11-18 with the
    /// maintainer's 208 b.
    /// </remarks>
    /// <param name="origin">What to call the file in a failure message.</param>
    /// <param name="member">Which half the row is in.</param>
    /// <param name="row">The row.</param>
    /// <param name="kind">Its verdict.</param>
    /// <returns>The refusals, or <see langword="null"/>.</returns>
    private static List<ArgumentRefusal>? Refusals(string origin, string member, JsonProperty row, ToolVerdictKind kind)
    {
        if (!row.Value.TryGetProperty(RefuseMember, out var refuse))
        {
            return null;
        }

        var at = $"'{member}.{row.Name}.{RefuseMember}'";

        if (kind is not ToolVerdictKind.Allow)
        {
            throw Unreadable(origin, $"'{member}.{row.Name}' carries a '{RefuseMember}' and its verdict is not 'allow'. An argument refusal narrows the calls of a tool BrowserAI forwards, and a tool it does not forward has no call to narrow");
        }

        if (refuse.ValueKind is not JsonValueKind.Array || refuse.GetArrayLength() is 0)
        {
            throw Unreadable(origin, $"{at} is not a non-empty array of argument refusals");
        }

        var refusals = new List<ArgumentRefusal>();

        foreach (var rule in refuse.EnumerateArray())
        {
            var where = $"{at}[{refusals.Count}]";

            if (rule.ValueKind is not JsonValueKind.Object)
            {
                throw Unreadable(origin, $"{where} is {rule.ValueKind}, not an object");
            }

            foreach (var field in rule.EnumerateObject())
            {
                if (!RefusalMembers.Contains(field.Name, StringComparer.Ordinal))
                {
                    throw Unreadable(origin, $"{where} carries '{field.Name}', and an argument refusal has exactly these members: {string.Join(", ", RefusalMembers)}");
                }
            }

            var setting = Required(origin, where, rule, WhileMember);

            if (!SessionRunSettings.Switches.Contains(setting, StringComparer.Ordinal))
            {
                throw Unreadable(origin, $"{where}.{WhileMember} is '{setting}', and a refusal is turned on by one of the per-run settings that are on or off: {string.Join(", ", SessionRunSettings.Switches)}");
            }

            refusals.Add(new ArgumentRefusal(
                Required(origin, where, rule, ArgumentMember),
                Values(origin, where, rule),
                setting,
                Required(origin, where, rule, RefusalMember),
                Required(origin, where, rule, WhyMember),
                Required(origin, where, rule, SinceMember),
                Required(origin, where, rule, UntilMember)));
        }

        return refusals;
    }

    private static string Required(string origin, string where, JsonElement rule, string member) =>
        rule.TryGetProperty(member, out var value) && value.ValueKind is JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw Unreadable(origin, $"{where} carries no '{member}' as a non-empty string");

    private static List<string> Values(string origin, string where, JsonElement rule)
    {
        if (!rule.TryGetProperty(ExceptMember, out var except))
        {
            return [];
        }

        if (except.ValueKind is not JsonValueKind.Array)
        {
            throw Unreadable(origin, $"{where}.{ExceptMember} is not an array of the values that do not turn the refusal on");
        }

        var values = new List<string>();

        foreach (var value in except.EnumerateArray())
        {
            values.Add(value.ValueKind is JsonValueKind.String && value.GetString() is { } text
                ? text
                : throw Unreadable(origin, $"{where}.{ExceptMember} holds {value.ValueKind}, and every value in it is a string"));
        }

        return values;
    }

    private static string? Optional(JsonElement row, string member) =>
        row.TryGetProperty(member, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static InvalidOperationException Unreadable(string origin, string what) =>
        new($"'{origin}' is not a tool-verdicts file this build can read: {what}. "
            + "Nothing was loaded, and BrowserAI will not serve with an empty verdict set -- deny-by-default would refuse every call and name nothing.");
}

/// <summary>Whether one call may proceed, and why not if it may not.</summary>
/// <remarks>
/// <para>
/// A refusal carries its text and not a code, because the audience is a model
/// deciding what to do next and every text in the catalogue names a fix.
/// </para>
/// <para>
/// ⚠️ <b>A withheld tool carries none, since 2026-10-10</b>, round 2 of the texts
/// review, #49: the answer a denied name gets is the one any name BrowserAI does not
/// have gets, with the tool list, and only the proxy holds the list. The judgement
/// built that answer with no list until then, so it said <i>listed below</i> over
/// nothing.
/// </para>
/// </remarks>
internal readonly record struct ToolDecision
{
    private ToolDecision(bool allowed, string? refusal)
    {
        IsAllowed = allowed;
        Refusal = refusal;
    }

    /// <summary>The call may proceed.</summary>
    public static ToolDecision Allowed { get; } = new(allowed: true, refusal: null);

    /// <summary>
    /// The tool is withheld from the surface: the call may not proceed, and it is
    /// answered as a tool BrowserAI does not have, by the one that holds the tool list.
    /// </summary>
    public static ToolDecision Withheld { get; } = new(allowed: false, refusal: null);

    /// <summary>
    /// Why the call was refused, or <see langword="null"/> when it was not, or when it
    /// is <see cref="Withheld"/> and the caller answers it.
    /// </summary>
    public string? Refusal { get; }

    /// <summary>Whether the call may proceed.</summary>
    public bool IsAllowed { get; }

    /// <summary>Builds a refusal.</summary>
    /// <param name="refusal">The text the caller reads.</param>
    /// <returns>The decision.</returns>
    public static ToolDecision Refused(string refusal) => new(allowed: false, refusal);
}

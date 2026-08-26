// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Frozen;
using System.Text.Json;

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
/// On a <see cref="ToolVerdictKind.Deny"/>, the reason a caller reads — the
/// whole of the refusal below BrowserAI's own first sentence. <see langword="null"/>
/// otherwise; a <c>deny</c> without one does not load.
/// </param>
/// <param name="Since">
/// On a <see cref="ToolVerdictKind.Deny"/>, the ISO date the judgement was made.
/// It is provenance for a person reading the file and is deliberately absent
/// from the refusal, which is written for a model deciding what to do next.
/// </param>
internal sealed record ToolVerdict(string Name, ToolVerdictKind Kind, string? Why, string? Since);

/// <summary>
/// Every tool BrowserAI knows of, read from the <c>tool-verdicts.json</c> that
/// ships beside the payload it describes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The list is data because the decision is ours and the tool set is
/// upstream's.</b> Until 2026-08-26 the whole of this was one C# constant —
/// <c>SessionToolPolicy.AnnotateTool</c>, a denylist of exactly one name, with a
/// refusal written out longhand in <see cref="SessionErrors"/> beside it. That
/// works for one tool and answers nothing about the other sixty-eight: a build
/// could not say <i>whether anybody had judged</i> a name, only whether it was
/// the one name it had heard of, so a tool upstream added arrived on this
/// server's surface and was forwarded before any human saw it.
/// </para>
/// <para>
/// <b>DENY BY DEFAULT, and the window that opens is bounded by a red build
/// rather than by a promise.</b> A name with no row is refused at the door —
/// <see cref="Decide"/> — because the alternative is that the file stops being a
/// gate and becomes an inventory. What makes that safe rather than a slow
/// capability leak is <c>ToolVerdictTests</c>: every tool in the golden
/// <c>tools-list.json</c> snapshot must have a row here and every row must name a
/// tool the snapshot carries, checked in both directions on every run. A
/// Playwright bump that adds a tool therefore reddens the suite in the same pass
/// that reddens the snapshot diff, and the two are adjudicated together.
/// </para>
/// <para>
/// ⚠️ <b>This reverses a decision taken 2026-08-18, and it reverses it for a
/// reason that decision's own reasoning does not reach.</b> A <c>(tool, mode)</c>
/// deny-by-default matrix was deleted that day as <i>security theatre</i> — it
/// was never a boundary against a caller who owns the session directory and reads
/// the profile inside it as the same user, and that argument is untouched and
/// still correct. What is different here is that a verdict is not a permission:
/// it decides <b>whether a name this build has never been told about is worth
/// starting a browser for</b>. Upstream looks a tool name up <i>after</i>
/// creating the browser context (<c>coreBundle.js</c>: the CLI factory's
/// <c>create</c> runs at <c>:73101</c>, the name lookup at <c>:65533</c>), so a
/// call naming nothing launches a browser to be told there is nothing to run —
/// and upstream's answer echoes the caller's own string back into model-facing
/// text. Neither of those is a permission question and neither was in scope on
/// 2026-08-18.
/// </para>
/// <para>
/// <b>A missing or malformed file is a loud failure and never an empty
/// allowlist.</b> Under deny-by-default an empty set denies everything, so a
/// silent fallback would present as <i>every tool suddenly refused</i> with
/// nothing anywhere naming the file. Every refusal below names the file and what
/// was wrong with it, and <see cref="Runtime.PayloadLayout.Verify"/> reports an
/// absent one as an incomplete payload before a child is ever started.
/// </para>
/// </remarks>
internal sealed class ToolVerdicts
{
    /// <summary>The file this is read from, at the repository root and in the payload.</summary>
    public const string FileName = "tool-verdicts.json";

    /// <summary>
    /// The only schema this build can read.
    /// </summary>
    /// <remarks>
    /// Checked rather than ignored: the file travels inside the payload, which an
    /// update replaces wholesale, so a binary meeting a shape it does not
    /// understand should say so rather than read the half it recognises.
    /// </remarks>
    public const int SchemaVersion = 1;

    private const string UpstreamMember = "upstream";
    private const string AuthoredMember = "authored";
    private const string VerdictMember = "verdict";
    private const string WhyMember = "why";
    private const string SinceMember = "since";
    private const string SchemaVersionMember = "schemaVersion";
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
    /// ⚠️ <b>These rows have no run-time effect at all, and that is stated here
    /// rather than left to be discovered — 2026-08-26.</b>
    /// <c>SessionToolSurface.IsAuthored</c> short-circuits every
    /// <c>browserai_</c> name in <c>BrowserProxy.AnswerToolsCallAsync</c> before
    /// <see cref="Decide"/> is reached, and <c>SessionToolSurface.Rewrite</c>
    /// advertises the authored tools from <c>SessionToolSurface.Names</c> rather
    /// than from the file — so removing an <c>answer</c> row changes nothing a
    /// caller can observe. <b>Their role is build-and-test-time:</b>
    /// <c>ToolVerdictTests.TheAuthoredRowsAreExactlyTheToolsBrowserAiAnswersItself</c>
    /// holds them identical to that surface in both directions, and
    /// <c>build/Write-ReleaseManifest.ps1</c> copies the whole file beside the
    /// release so a rollback can read which tools a build forwarded and which
    /// upstream that judgement was made against. The <see cref="Upstream"/> half
    /// is the opposite: it decides every call.
    /// </remarks>
    public IReadOnlyList<ToolVerdict> Authored { get; }

    /// <summary>
    /// The package versions the judgements were made against.
    /// </summary>
    /// <remarks>
    /// Nothing at run time compares this with the child that is actually running:
    /// the file travels inside the payload it describes, so the two move
    /// together or the install is broken in a way this check could not repair.
    /// What compares it is <c>ToolVerdictTests</c>, against the committed payload
    /// lock, which is where a disagreement is still fixable.
    /// </remarks>
    public IReadOnlyDictionary<string, string> JudgedAgainst { get; }

    /// <summary>Reads the file from disk.</summary>
    /// <param name="path">The file, named absolutely.</param>
    /// <returns>The verdicts.</returns>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    /// <exception cref="InvalidOperationException">There is one and this build cannot read it.</exception>
    public static ToolVerdicts Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The payload is incomplete: '{path}' does not exist, so BrowserAI cannot tell which tools it is allowed to forward. "
                + "Every call would be refused. Run build/Build-Payload.ps1 and rebuild, or reinstall.",
                path);
        }

        return Parse(File.ReadAllBytes(path), path);
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
                throw Unreadable(origin, $"its root is {root.ValueKind} rather than an object");
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
    /// disabled</b> — no entry, no description explaining that it will refuse,
    /// nothing for a model to read and weigh — because a tool that can never
    /// succeed costs attention and description budget for as long as it is in the
    /// list. A tool with <i>no</i> row is a different thing: it is a gap rather
    /// than a decision, so it is still advertised and refused at the door. Two
    /// reasons, and the second is the stronger. A gap is already loud — the
    /// coverage comparison is red on the same build — so the advertisement adds
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
    /// first sentence — <i>this build was told not to forward it, here is what to
    /// do instead</i>. A name with no row answers that this build has no verdict
    /// at all — <i>this is a gap, call <c>tools/list</c></i>. Collapsing them
    /// would send a model looking for a permission to acquire in the one case and
    /// for a typo in the other.
    /// </para>
    /// <para>
    /// <b>An <c>answer</c> row cannot reach here and is refused rather than
    /// allowed if it ever does.</b> <c>SessionToolSurface.IsAuthored</c>
    /// short-circuits every <c>browserai_</c> name before the door, and the loader
    /// refuses an <c>answer</c> row whose name is not one — so the branch is
    /// unreachable by construction, and the direction it fails in is the one that
    /// does not forward an authored name to a child that has never heard of it.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool the caller named.</param>
    /// <returns>Permission, or a refusal a model can act on in one turn.</returns>
    public ToolDecision Decide(string? tool) =>
        Find(tool) switch
        {
            { Kind: ToolVerdictKind.Allow } => ToolDecision.Allowed,
            { Kind: ToolVerdictKind.Deny } denied => ToolDecision.Refused(SessionErrors.ToolIsDenied(denied.Name, denied.Why!)),
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
            throw Unreadable(origin, $"'{member}.{row.Name}' is {row.Value.ValueKind} rather than an object");
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
        // marked `allow` claims a child could run it.
        if (authored && kind is not ToolVerdictKind.Answer)
        {
            throw Unreadable(
                origin,
                $"'{member}.{row.Name}' is one of BrowserAI's own tools and carries the verdict '{word.GetString()}'. Authored tools are always 'answer' -- there is no child to forward one to");
        }

        if (authored && !SessionToolSurface.IsAuthored(row.Name))
        {
            throw Unreadable(
                origin,
                $"'{member}.{row.Name}' is in '{AuthoredMember}' and is not one of BrowserAI's own tools -- those all begin '{SessionToolSurface.Prefix}'");
        }

        if (!authored && kind is ToolVerdictKind.Answer)
        {
            throw Unreadable(
                origin,
                $"'{member}.{row.Name}' carries 'answer', which says BrowserAI implements it. Upstream's tools are 'allow' or 'deny'");
        }

        var why = Optional(row.Value, WhyMember);
        var since = Optional(row.Value, SinceMember);

        if (kind is not ToolVerdictKind.Deny)
        {
            return new ToolVerdict(row.Name, kind, why, since);
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
/// A refusal carries its text rather than a code, because the audience is a model
/// deciding what to do next and every text in the catalogue names a fix.
/// </remarks>
internal readonly record struct ToolDecision
{
    private ToolDecision(string? refusal) => Refusal = refusal;

    /// <summary>The call may proceed.</summary>
    public static ToolDecision Allowed { get; } = new(null);

    /// <summary>Why the call was refused, or <see langword="null"/> when it was not.</summary>
    public string? Refusal { get; }

    /// <summary>Whether the call may proceed.</summary>
    public bool IsAllowed => Refusal is null;

    /// <summary>Builds a refusal.</summary>
    /// <param name="refusal">The text the caller reads.</param>
    /// <returns>The decision.</returns>
    public static ToolDecision Refused(string refusal) => new(refusal);
}

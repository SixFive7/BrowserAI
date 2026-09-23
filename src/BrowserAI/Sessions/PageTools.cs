// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json.Nodes;

namespace BrowserAI.Sessions;

/// <summary>
/// One tool the page under a session's current tab offers, as it arrives on the
/// child's own <c>tools/list</c>.
/// </summary>
/// <param name="WireName">
/// The name the child answers to -- <c>webmcp_</c> and a sanitised copy of what
/// the page called the tool. It is upstream's spelling and BrowserAI never
/// invents one.
/// </param>
/// <param name="Title">
/// <c>annotations.title</c>, which is the page's own <c>title</c> when it set
/// one and the page's tool NAME when it did not.
/// </param>
internal sealed record PageTool(string WireName, string? Title);

/// <summary>
/// What one <c>browserai_page_tool</c> call resolved to: the <c>tools/call</c>
/// to forward, or the refusal that says why there is none.
/// </summary>
/// <remarks>
/// <b>The two names are carried even on the way to a success</b>, because the
/// call can still be abandoned on the timeout and that refusal has to say both
/// what the caller asked for and what was actually sent -- a session log read
/// afterwards shows the wire name and nothing else.
/// </remarks>
/// <param name="Name">The page's own name for the tool, as the caller typed it.</param>
/// <param name="WireName">The name the child was, or would have been, asked for.</param>
/// <param name="Call">The <c>tools/call</c> parameters, when there is a call to make.</param>
/// <param name="Refusal">What the caller is told instead, when there is not.</param>
internal sealed record PageToolResolution(string Name, string WireName, JsonObject? Call, string? Refusal)
{
    /// <summary>A resolution that ends in a refusal.</summary>
    /// <param name="refusal">The sentence the caller reads.</param>
    /// <returns>The resolution.</returns>
    public static PageToolResolution Refused(string refusal) =>
        new(string.Empty, string.Empty, null, refusal);

    /// <summary>A resolution that ends in a call.</summary>
    /// <param name="name">The page's own name for the tool.</param>
    /// <param name="wireName">The name the child answers to.</param>
    /// <param name="call">The <c>tools/call</c> parameters.</param>
    /// <returns>The resolution.</returns>
    public static PageToolResolution Forwarding(string name, string wireName, JsonObject call) =>
        new(name, wireName, call, null);
}

/// <summary>
/// How a name a model read in a snapshot becomes a name the child answers to.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a hand-written upstream schema and it is not a rename.</b>
/// Nothing here describes a tool: it reproduces the one function upstream uses
/// to turn a page's own tool name into a wire name, so that a call naming the
/// tool a model actually read about reaches the tool it read about. Every
/// definition still comes from the child's <c>tools/list</c> at run time, and the
/// name that goes back out is the child's own, byte for byte.
/// </para>
/// <para>
/// ⚠️ <b>Resolution is by WIRE NAME and the cross-check is
/// <c>annotations.title</c>, which is the opposite way round from how this was
/// specified -- because <c>title</c> is not the page's tool name.</b> Read out of
/// the resolved bundle and then measured, 2026-09-21 @ <c>@playwright/mcp</c>
/// 0.0.82 / <c>playwright-core</c> 1.64.0-alpha-1789764292000: upstream builds
/// the entry with <c>title: tool.title || tool.name</c>, so a page that supplies
/// its own <c>title</c> puts THAT in the annotations -- while the snapshot block a
/// model reads the tool out of prints <c>tool.name</c>. Measured against a page
/// registering <c>{ name: "raw_name_here", title: "Human Title" }</c>: the
/// snapshot block printed <c>raw_name_here</c>, the wire name was
/// <c>webmcp_raw_name_here</c>, and <c>annotations.title</c> was
/// <c>Human Title</c>. Matching on the title would therefore have made every
/// page tool that sets one uncallable, while the wire name is a pure function of
/// the name the model actually read.
/// </para>
/// <para>
/// <b>Re-resolved on every call, never cached.</b> The child's list covers the
/// CURRENT TAB only and the same wire name resolves to a different page's code
/// after a navigation -- see the late-binding row in <c>HAZARDS.md</c>. A
/// resolution kept between calls would be a name pointing at whatever the tab
/// happens to be showing now.
/// </para>
/// </remarks>
internal static class PageTools
{
    /// <summary>What upstream puts in front of every page-supplied tool name.</summary>
    public const string WirePrefix = "webmcp_";

    /// <summary>
    /// The shape of the <c>browser_tabs</c> line that names the tab a page tool
    /// would run in.
    /// </summary>
    /// <remarks>
    /// <b>Upstream's own marker, measured rather than assumed</b> -- a tab list
    /// line carries the index, then this marker on the current tab, then the
    /// page title in square brackets and the URL in round ones, and a run with
    /// one tab still carries the marker. Measured 2026-09-21 @
    /// <c>@playwright/mcp</c> 0.0.82.
    /// </remarks>
    public const string CurrentTabMarker = "(current)";

    /// <summary>
    /// What upstream cuts a sanitised page tool name at, in UTF-16 code units.
    /// </summary>
    /// <remarks>
    /// <c>name.replace(/[^a-zA-Z0-9_-]/g, "_").slice(0, 64) || "tool"</c>, read
    /// from <c>sanitizeToolName</c> in the resolved bundle. JavaScript's
    /// <c>slice</c> counts UTF-16 code units, which is what a .NET string index
    /// counts, so the two cut in the same place -- including in the middle of a
    /// surrogate pair, which neither of them guards against.
    /// </remarks>
    public const int SanitisedMaximumLength = 64;

    /// <summary>What upstream falls back to when sanitising leaves nothing.</summary>
    public const string SanitisedFallback = "tool";

    /// <summary>
    /// The name the child answers to, for a page tool a model read by name.
    /// </summary>
    /// <param name="pageToolName">The name the snapshot block printed, verbatim.</param>
    /// <returns>The wire name upstream would have built for it.</returns>
    public static string WireNameFor(string pageToolName)
    {
        ArgumentNullException.ThrowIfNull(pageToolName);

        var sanitised = new StringBuilder(pageToolName.Length);

        foreach (var character in pageToolName)
        {
            sanitised.Append(IsKept(character) ? character : '_');
        }

        var cut = sanitised.Length > SanitisedMaximumLength
            ? sanitised.ToString(0, SanitisedMaximumLength)
            : sanitised.ToString();

        return WirePrefix + (cut.Length is 0 ? SanitisedFallback : cut);
    }

    /// <summary>
    /// Whether a wire name is the one <paramref name="expected"/> names, or one
    /// of upstream's de-duplicated spellings of it.
    /// </summary>
    /// <remarks>
    /// <b>The suffix is upstream's collision rule, not ours.</b> A page
    /// registering two tools whose sanitised names collide gets
    /// <c>&lt;base&gt;</c>, <c>&lt;base&gt;_2</c>, <c>&lt;base&gt;_3</c> and so
    /// on -- measured 2026-09-21 against a page registering <c>twin</c> twice,
    /// which produced <c>webmcp_twin</c> and <c>webmcp_twin_2</c> with the same
    /// <c>annotations.title</c> on both. Both are matches, and a caller naming
    /// that tool gets the ambiguity refusal rather than a silently chosen one.
    /// </remarks>
    /// <param name="wireName">The name on the child's list.</param>
    /// <param name="expected">What <see cref="WireNameFor"/> produced.</param>
    /// <returns>Whether the two name the same page tool.</returns>
    public static bool WireNameMatches(string? wireName, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        if (wireName is null)
        {
            return false;
        }

        if (string.Equals(wireName, expected, StringComparison.Ordinal))
        {
            return true;
        }

        if (wireName.Length <= expected.Length + 1
            || !wireName.StartsWith(expected, StringComparison.Ordinal)
            || wireName[expected.Length] is not '_')
        {
            return false;
        }

        var suffix = wireName[(expected.Length + 1)..];

        return suffix.All(char.IsAsciiDigit) && suffix[0] is not '0';
    }

    /// <summary>Every page-supplied tool on a child's <c>tools/list</c> result.</summary>
    /// <remarks>
    /// <b>Identified by upstream's prefix and nothing else.</b> There is no
    /// structural marker on the wire -- a page tool carries the same four members
    /// a static one does -- so the prefix is the discriminator, and it is the one
    /// upstream itself composes the name from. A static tool that ever began
    /// <c>webmcp_</c> would be caught by the golden snapshot diff first.
    /// </remarks>
    /// <param name="result">The child's <c>tools/list</c> result.</param>
    /// <returns>The page tools, in the order the child listed them.</returns>
    public static IReadOnlyList<PageTool> In(JsonObject? result)
    {
        var found = new List<PageTool>();

        foreach (var tool in result?["tools"] as JsonArray ?? [])
        {
            if (tool is not JsonObject definition
                || (definition["name"] as JsonValue)?.GetValueKind() is not System.Text.Json.JsonValueKind.String
                || definition["name"]!.GetValue<string>() is not { } name
                || !name.StartsWith(WirePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var title = (definition["annotations"] as JsonObject)?["title"];

            found.Add(new PageTool(
                name,
                title?.GetValueKind() is System.Text.Json.JsonValueKind.String ? title.GetValue<string>() : null));
        }

        return found;
    }

    /// <summary>
    /// How a page tool is written in a refusal: what the page called it if that
    /// is knowable, and the wire name either way.
    /// </summary>
    /// <remarks>
    /// <b>Both, because neither alone is enough.</b> The wire name is what the
    /// child holds and is sanitised, so it is not always the name the snapshot
    /// printed; the title is the page's own text and is only the tool's name when
    /// the page set no separate <c>title</c>. A caller comparing a refusal
    /// against the snapshot block it read needs whichever of the two happens to
    /// be the name it typed.
    /// </remarks>
    /// <param name="tool">The page tool.</param>
    /// <returns>One line naming it.</returns>
    public static string Describe(PageTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return tool.Title is { Length: > 0 } title
            ? $"'{title}' (on the wire as {tool.WireName})"
            : tool.WireName;
    }

    /// <summary>
    /// The URL of the tab a page tool would run in, read out of upstream's own
    /// tab listing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Parsed rather than asked for, because there is nothing to ask.</b> No
    /// tool in the surface answers <i>what is the current tab's URL</i> as data;
    /// <c>browser_tabs</c> answers it as one Markdown line per tab, and the tab
    /// carrying <see cref="CurrentTabMarker"/> is the one a page-tool call would
    /// reach.
    /// </para>
    /// <para>
    /// ⚠️ <b>The URL is taken from the LAST <c>](</c> on the line, not the
    /// first.</b> The title in front of it is the page's own text and can contain
    /// a <c>](</c> of its own -- a page titled <c>a](http://not-this-one)</c>
    /// would otherwise hand a caller a URL it invented. The last one is the link
    /// target because upstream writes the URL last and closes the line with
    /// <c>)</c>.
    /// </para>
    /// <para>
    /// <b>A line it cannot read yields <see langword="null"/> rather than a
    /// guess</b>, and the caller refuses the call and says it could not check.
    /// </para>
    /// </remarks>
    /// <param name="tabs">Everything <c>browser_tabs</c> said.</param>
    /// <returns>The current tab's URL, or <see langword="null"/> when no line names one.</returns>
    public static string? CurrentTabUrl(string? tabs)
    {
        foreach (var line in (tabs ?? string.Empty).Split('\n'))
        {
            var trimmed = line.TrimEnd('\r', ' ');

            if (!trimmed.Contains(CurrentTabMarker, StringComparison.Ordinal) || !trimmed.EndsWith(')'))
            {
                continue;
            }

            var opened = trimmed.LastIndexOf("](", StringComparison.Ordinal);

            if (opened < 0)
            {
                continue;
            }

            var url = trimmed[(opened + 2)..^1];

            if (url.Length is not 0)
            {
                return url;
            }
        }

        return null;
    }

    private static bool IsKept(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '_' or '-';
}

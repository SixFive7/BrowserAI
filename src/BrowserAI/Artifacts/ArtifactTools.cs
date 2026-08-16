// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;

namespace BrowserAI.Artifacts;

/// <summary>What a tool's <c>filename</c> argument names.</summary>
internal enum ArtifactArgument
{
    /// <summary>A file the tool is about to write. Routed by generator prefix.</summary>
    Written,

    /// <summary>A file the tool is about to read. Resolved the same way, never suffixed.</summary>
    Read,

    /// <summary>
    /// A file this design has no prefix for. Validated for shape and otherwise
    /// left alone.
    /// </summary>
    Opaque,
}

/// <summary>How one tool's <c>filename</c> argument is handled.</summary>
/// <param name="Tool">The upstream tool name.</param>
/// <param name="Kind">What the argument names.</param>
/// <param name="Prefix">
/// The generator prefix upstream would have used, given the call's own
/// arguments. Empty for <see cref="ArtifactArgument.Opaque"/>.
/// </param>
/// <param name="GeneratedExtension">
/// The extension to use when BrowserAI supplies a name upstream would otherwise
/// have generated, or <see langword="null"/> when it must never supply one.
/// </param>
internal sealed record ArtifactToolRule(
    string Tool,
    ArtifactArgument Kind,
    Func<JsonObject?, string> Prefix,
    Func<JsonObject?, string>? GeneratedExtension);

/// <summary>
/// Every tool whose <c>filename</c> argument BrowserAI has judged, and what it
/// decided. Deny by default.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same rule as the session-type policy, applied to files.</b> A tool in
/// the resolved surface carrying a <c>filename</c> that this table does not
/// classify fails the build — <c>ArtifactRoutingTests</c> reads the committed
/// <c>tools/list</c> snapshot and asserts it. An unjudged <c>filename</c> is a
/// file landing wherever upstream's default happens to put it, which is the flat
/// output directory [§F](../../plan/F-artifacts.md) exists to replace.
/// </para>
/// <para>
/// <b>A name is supplied only where upstream would have generated one</b>, and
/// that distinction is load-bearing rather than cosmetic. Five of these tools
/// document <c>filename</c> as <i>"if not provided, the result is returned as
/// text"</i> — for those, supplying a name would silently change what the tool
/// does, taking the answer out of the response and putting it in a file the
/// caller never asked for. The four that say <i>"defaults to
/// page-{timestamp}.png"</i> always write, so a name supplied there replaces a
/// timestamp nobody can read with one derived from the page.
/// </para>
/// <para>
/// <b>Two prefixes depend on the call's other arguments</b>, which is why this
/// is a function rather than a constant. A screenshot with a <c>target</c> is an
/// <c>element</c> and without one is a <c>page</c>; a network request asked for
/// a response part is a <c>response</c> and otherwise a <c>request</c>. Both
/// were read out of the resolved bundle, where they are a ternary and a
/// parameter switch.
/// </para>
/// </remarks>
internal static class ArtifactTools
{
    /// <summary>The argument every one of these tools carries.</summary>
    public const string FilenameArgument = "filename";

    /// <summary>The tools whose <c>filename</c> has been judged, by name.</summary>
    public static IReadOnlyDictionary<string, ArtifactToolRule> Rules { get; } =
        new ArtifactToolRule[]
        {
            // Always writes; upstream would generate the name. BrowserAI
            // supplies a legible one instead.
            Written("browser_take_screenshot", ScreenshotPrefix, ScreenshotExtension),
            Written("browser_pdf_save", _ => "page", _ => "pdf"),
            Written("browser_storage_state", _ => "storage-state", _ => "json"),

            // .webm is not a preference: the recorder throws on any other
            // extension.
            Written("browser_start_video", _ => "video", _ => "webm"),

            // Writes only when asked. Routed when a name is given, and never
            // given one, because the argument's presence is what decides whether
            // the answer is a file or the response body.
            Written("browser_console_messages", _ => "console", generatedExtension: null),
            Written("browser_evaluate", _ => "result", generatedExtension: null),
            Written("browser_network_requests", _ => "network", generatedExtension: null),
            Written("browser_network_request", NetworkRequestPrefix, generatedExtension: null),
            Written("browser_snapshot", _ => "page", generatedExtension: null),

            // Reads the file `browser_storage_state` wrote, so it resolves the
            // same way. Routing it is what makes that round trip work at all.
            new ArtifactToolRule("browser_set_storage_state", ArtifactArgument.Read, _ => "storage-state", null),

            // A code file the caller put somewhere itself. There is no prefix
            // for it and inventing one would break the tool, so it is validated
            // for shape and resolved against the output root, which is upstream's
            // own allowed root.
            new ArtifactToolRule("browser_run_code_unsafe", ArtifactArgument.Opaque, _ => string.Empty, null),
        }.ToDictionary(rule => rule.Tool, StringComparer.Ordinal);

    /// <summary>The rule for one tool, if this build has judged it.</summary>
    /// <param name="tool">The tool name.</param>
    /// <returns>The rule, or <see langword="null"/>.</returns>
    public static ArtifactToolRule? For(string? tool) =>
        tool is not null && Rules.TryGetValue(tool, out var rule) ? rule : null;

    private static ArtifactToolRule Written(
        string tool,
        Func<JsonObject?, string> prefix,
        Func<JsonObject?, string>? generatedExtension) =>
        new(tool, ArtifactArgument.Written, prefix, generatedExtension);

    /// <summary>
    /// <c>prefix: target ? "element" : "page"</c>, read from the resolved
    /// bundle.
    /// </summary>
    private static string ScreenshotPrefix(JsonObject? arguments) =>
        arguments?["target"] is not null ? "element" : "page";

    /// <summary>
    /// The image format, which upstream takes from <c>type</c> first and from
    /// the file name's extension second.
    /// </summary>
    /// <remarks>
    /// Reading <c>type</c> is what stops a supplied name overriding the caller's
    /// choice: <c>fileType = params.type ?? fromExtension(filename) ?? "png"</c>,
    /// so a <c>.png</c> name handed to a call asking for <c>jpeg</c> would
    /// produce jpeg bytes in a file called png.
    /// </remarks>
    private static string ScreenshotExtension(JsonObject? arguments) =>
        (arguments?["type"] as JsonValue)?.TryGetValue(out string? type) is true && type is { Length: > 0 }
            ? type
            : "png";

    /// <summary>
    /// <c>request</c> unless the caller asked for one of the response parts, in
    /// which case upstream writes it with the <c>response</c> prefix.
    /// </summary>
    private static string NetworkRequestPrefix(JsonObject? arguments) =>
        (arguments?["part"] as JsonValue)?.TryGetValue(out string? part) is true
        && part?.StartsWith("response", StringComparison.Ordinal) is true
            ? "response"
            : "request";
}

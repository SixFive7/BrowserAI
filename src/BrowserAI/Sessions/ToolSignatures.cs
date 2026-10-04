// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserAI.Sessions;

/// <summary>
/// The arguments every tool in the surface takes, read off the surface a caller
/// was given and from nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decided 2026-10-03 by the maintainer, in his words:</b> <i>"I'd expect that
/// any call carrying any parameter or argument that we do not recognize would be
/// refused actively with a syntax error. This would teach the LLM it has somethign
/// wrong. Also, I do not like us keeping history and translating certen arguments
/// for historical sake. The product is what it is and the llm needs to learn to use
/// it."</i> Measured 2026-10-03 through the published binary at <c>d8a0101a</c>,
/// before this existed: an argument no schema has was dropped without a word by
/// <c>browserai_list</c> and by <c>browser_navigate</c>, and the call went ahead.
/// </para>
/// <para>
/// ⚠️ <b>Read from the REWRITTEN <c>tools/list</c>, never written by hand.</b> A
/// forwarded tool's schema is upstream's, with <c>session</c> and <c>why</c>
/// added by <see cref="SessionToolSurface.Rewrite"/>, and an authored tool's is
/// BrowserAI's own -- both arrive in the one list, so the check and the list a
/// model read cannot disagree. The scope rule forbids a hand-written upstream
/// schema anywhere in the product, and this is the reason it would matter here:
/// a table typed into C# would go on accepting what upstream removed.
/// </para>
/// <para>
/// <b>Names, and never <c>required</c>.</b> Upstream's schemas mark a parameter
/// that has a default as required -- <c>browser_take_screenshot</c>'s
/// <c>scale</c> is one, in the snapshot read 2026-10-04 -- so refusing a call for
/// leaving one out would refuse calls upstream accepts. What is checked is that
/// every name a call carries is a name its schema lists.
/// </para>
/// <para>
/// <b>Each tool keeps its whole definition</b>, as the list carried it, so that a
/// refusal can be given a block generated from the live list if that is ever
/// wanted. It is a copy: the list itself goes to the caller.
/// </para>
/// </remarks>
internal sealed class ToolSignatures
{
    private const string ToolsMember = "tools";
    private const string NameMember = "name";
    private const string SchemaMember = "inputSchema";
    private const string PropertiesMember = "properties";
    private const string RequiredMember = "required";

    private readonly FrozenDictionary<string, ToolSignature> _tools;

    private ToolSignatures(FrozenDictionary<string, ToolSignature> tools, bool carriesTheChildsTools)
    {
        _tools = tools;
        CarriesTheChildsTools = carriesTheChildsTools;
    }

    /// <summary>
    /// Whether the list this was read from carried the run's own child's tools,
    /// and not only BrowserAI's own.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> when the child could not be asked for its list:
    /// the authored tools are still checked, and a forwarded tool is then judged
    /// by the verdicts file alone.
    /// </remarks>
    public bool CarriesTheChildsTools { get; }

    /// <summary>Reads a <c>tools/list</c> result.</summary>
    /// <param name="result">The result, as BrowserAI answers it -- rewritten.</param>
    /// <param name="carriesTheChildsTools">Whether the child's own tools are in it.</param>
    /// <returns>Every tool it names, with what it takes.</returns>
    public static ToolSignatures From(JsonObject result, bool carriesTheChildsTools = true)
    {
        ArgumentNullException.ThrowIfNull(result);

        var tools = new Dictionary<string, ToolSignature>(StringComparer.Ordinal);

        foreach (var node in result[ToolsMember] as JsonArray ?? [])
        {
            if (node is not JsonObject tool
                || tool[NameMember] is not JsonValue name
                || name.GetValueKind() is not JsonValueKind.String)
            {
                continue;
            }

            var schema = tool[SchemaMember] as JsonObject;

            var arguments = (schema?[PropertiesMember] as JsonObject ?? [])
                .Select(property => property.Key)
                .ToList();

            var required = (schema?[RequiredMember] as JsonArray ?? [])
                .OfType<JsonValue>()
                .Where(entry => entry.GetValueKind() is JsonValueKind.String)
                .Select(entry => entry.GetValue<string>())
                .ToFrozenSet(StringComparer.Ordinal);

            var signature = new ToolSignature
            {
                Name = name.GetValue<string>(),
                Arguments = arguments,
                Required = required,
                Definition = (JsonObject)tool.DeepClone(),
            };

            tools[signature.Name] = signature;
        }

        return new ToolSignatures(tools.ToFrozenDictionary(StringComparer.Ordinal), carriesTheChildsTools);
    }

    /// <summary>One tool's signature, or <see langword="null"/> when the list does not carry it.</summary>
    /// <param name="tool">The name a call carried.</param>
    /// <returns>The signature, or <see langword="null"/>.</returns>
    public ToolSignature? Find(string? tool) =>
        tool is not null && _tools.TryGetValue(tool, out var signature) ? signature : null;
}

/// <summary>What one tool in the surface takes.</summary>
internal sealed record ToolSignature
{
    /// <summary>The tool's name, as the list spells it.</summary>
    public required string Name { get; init; }

    /// <summary>Every argument it takes, in the order its schema lists them.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>The arguments its schema marks required.</summary>
    public required IReadOnlySet<string> Required { get; init; }

    /// <summary>The tool's whole definition, as the list carried it.</summary>
    public required JsonObject Definition { get; init; }

    /// <summary>
    /// Every argument a call carried that this tool does not take, in the order
    /// the call carried them.
    /// </summary>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>The names its schema does not list; empty when there are none.</returns>
    public IReadOnlyList<string> Unrecognised(JsonObject? arguments) =>
        arguments is null
            ? []
            : [.. arguments.Select(argument => argument.Key).Where(name => !Arguments.Contains(name, StringComparer.Ordinal))];

    /// <summary>
    /// The tool's definition as text: its description, then every argument with
    /// what its schema says of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q371.5 b, decided by the maintainer on 2026-10-03</b>: the refusal of an
    /// argument a schema does not have carries the tool's whole current
    /// definition, as <c>tools/list</c> serves it. Every word below comes out of
    /// <see cref="Definition"/>: the description, and per argument whether it is
    /// required, its type, its allowed values, its default and its own
    /// description. Nothing is added and nothing is reworded.
    /// </para>
    /// <para>
    /// <b>Required is as the schema says</b>, and upstream's schemas mark an
    /// argument with a default as required; the default is printed beside it, so
    /// a reader can see the two together.
    /// </para>
    /// </remarks>
    /// <returns>The definition, one argument per line, with no trailing line break.</returns>
    public string Rendered()
    {
        var text = new StringBuilder(Name);

        if (Text(Definition["description"]) is { Length: > 0 } description)
        {
            _ = text.Append(": ").Append(description);
        }

        if (Arguments.Count is 0)
        {
            return text.Append("\nIt takes no arguments.").ToString();
        }

        _ = text.Append("\nArguments:");

        var properties = Definition["inputSchema"]?["properties"] as JsonObject;

        foreach (var name in Arguments)
        {
            var property = properties?[name] as JsonObject;
            var facts = new List<string>();

            if (Required.Contains(name))
            {
                facts.Add("required");
            }

            if (Text(property?["type"]) is { Length: > 0 } type)
            {
                facts.Add(type);
            }

            if (property?["enum"] is JsonArray values)
            {
                facts.Add($"one of {string.Join(", ", values.Select(Shown))}");
            }

            if (property?.ContainsKey("default") is true)
            {
                facts.Add($"default {Shown(property["default"])}");
            }

            _ = text.Append("\n- '").Append(name).Append('\'');

            if (facts.Count is not 0)
            {
                _ = text.Append(" (").AppendJoin(", ", facts).Append(')');
            }

            if (Text(property?["description"]) is { Length: > 0 } said)
            {
                _ = text.Append(": ").Append(said);
            }
        }

        return text.ToString();
    }

    /// <summary>A node's string value, or <see langword="null"/> when it is not a string.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() is JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>A value as a reader writes it: a string in quotes, anything else as its JSON.</summary>
    /// <param name="node">The value.</param>
    /// <returns>The text.</returns>
    private static string Shown(JsonNode? node) =>
        Text(node) is { } text ? $"'{text}'" : node?.ToJsonString() ?? "null";
}

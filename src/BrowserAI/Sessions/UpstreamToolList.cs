// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Proxy;

namespace BrowserAI.Sessions;

/// <summary>
/// Upstream's half of the tool list, compiled into this binary: what
/// <c>tools/list</c> is answered from, and what each session's child is checked
/// against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Step 1 of the one-binary plan, built 2026-10-08.</b> The maintainer's words
/// of 2026-10-04, verbatim: <i>"I'd argue that the relay always answers the tool
/// list from the binary. I see no reason why it would ever defer to Playwright, as
/// the Playwright version is bound to that binary version is it not?"</i> It is:
/// the payload and the binary are packed together and replaced together, so the
/// list compiled in and the Playwright it describes cannot come from two versions.
/// Until that day a child of the run's own, started with every capability before
/// the handshake was answered, was asked <c>tools/list</c> on every call to it.
/// </para>
/// <para>
/// <b>The source is <c>upstream-snapshots/tools-list.json</c></b>, which
/// <c>build/upstream-snapshots.mjs</c> writes from the payload's own child and the
/// build compares with the payload on every run. Its <c>tools</c> member is what the
/// child answered, in the child's order, written by <c>JSON.stringify</c> with an
/// indent of two. The child writes the same value with no indent, so taking the
/// indentation out gives the child's own bytes: measured 2026-10-08 at
/// <c>@playwright/mcp</c> 0.0.83, five session configurations, Chromium and Firefox,
/// headless and headed, a transcript and a network capture, each answered the
/// 45,612 bytes this produces, byte for byte
/// ([kb](../../../kb/playwright/tools-and-artifacts.md#the-list-compiled-into-the-binary-is-the-childs-own-bytes----measured-2026-10-08)).
/// </para>
/// <para>
/// <b>So the check is byte for byte</b>: each session's child is asked
/// <c>tools/list</c> right after its handshake, before it has opened a page, and the
/// result bytes it wrote are compared with <see cref="ResultBytes"/>. A page's own
/// tools never take part, because no page exists when the question is asked. A
/// difference is a broken install, and the session does not open.
/// </para>
/// </remarks>
internal sealed class UpstreamToolList
{
    /// <summary>The name the build gives the embedded snapshot.</summary>
    public const string ResourceName = "BrowserAI.upstream-snapshots.tools-list.json";

    private const string ToolsMember = "tools";
    private const string NameMember = "name";

    private static readonly byte[] Opening = "{\"tools\":"u8.ToArray();

    private readonly byte[] _result;
    private readonly byte[][] _tools;
    private readonly string?[] _names;

    private UpstreamToolList(string origin, byte[] result, byte[][] tools, string?[] names)
    {
        Origin = origin;
        _result = result;
        _tools = tools;
        _names = names;
    }

    /// <summary>The list this binary was built with.</summary>
    /// <exception cref="InvalidOperationException">The build embedded no snapshot, or one this code cannot read.</exception>
    public static UpstreamToolList Compiled { get; } = FromResource();

    /// <summary>Where this list was read from, for a failure that names it.</summary>
    public string Origin { get; }

    /// <summary>How many tools it holds.</summary>
    public int Count => _tools.Length;

    /// <summary>The bytes a correctly installed child writes as its <c>tools/list</c> result.</summary>
    public ReadOnlySpan<byte> ResultBytes => _result;

    /// <summary>Reads a list from a snapshot document or a <c>tools/list</c> result.</summary>
    /// <remarks>
    /// <b>Either shape, because both carry the tools as a root member named
    /// <c>tools</c></b>: the committed snapshot carries counts and provenance beside
    /// it, and a result carries nothing else. Every other member is ignored, and the
    /// value of <c>tools</c> is kept as written with the whitespace outside its
    /// strings taken out.
    /// </remarks>
    /// <param name="json">The document.</param>
    /// <param name="origin">What to call it in a failure.</param>
    /// <returns>The list.</returns>
    /// <exception cref="InvalidOperationException">There is no <c>tools</c> array at the root.</exception>
    public static UpstreamToolList Parse(ReadOnlySpan<byte> json, string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        var (start, end) = ToolsValue(json)
            ?? throw new InvalidOperationException($"'{origin}' carries no 'tools' array at its root, so BrowserAI has no tool list to answer with.");

        var compact = Compact(json[start..end]);
        var tools = Elements(compact);

        var result = new byte[Opening.Length + compact.Length + 1];
        Opening.CopyTo(result, 0);
        compact.CopyTo(result, Opening.Length);
        result[^1] = (byte)'}';

        return new UpstreamToolList(origin, result, [.. tools.Select(range => compact[range.Start..range.End])], [.. tools.Select(range => NameIn(compact.AsSpan(range.Start, range.End - range.Start)))]);
    }

    /// <summary>A fresh copy of the result, for the rewrite to change.</summary>
    /// <returns>The result, parsed from the bytes and shared with nothing.</returns>
    public JsonObject Result() => JsonNode.Parse(_result)!.AsObject();

    /// <summary>
    /// Where a child's own <c>tools/list</c> result differs from this list, or
    /// <see langword="null"/> when the two are the same bytes.
    /// </summary>
    /// <param name="childResult">The result bytes exactly as the child wrote them.</param>
    /// <returns>The first difference, naming a tool, or <see langword="null"/>.</returns>
    public string? FirstDifference(ReadOnlySpan<byte> childResult)
    {
        if (childResult.SequenceEqual(_result))
        {
            return null;
        }

        if (ToolsValue(childResult) is not { } array)
        {
            return "its answer, which holds no list of tools";
        }

        var theirs = childResult[array.Start..array.End];
        var ranges = Elements(theirs);

        for (var index = 0; index < Math.Max(_tools.Length, ranges.Count); index++)
        {
            if (index >= ranges.Count)
            {
                return $"'{_names[index]}', which this BrowserAI lists and the browser server does not";
            }

            var their = theirs[ranges[index].Start..ranges[index].End];
            var name = NameIn(their);

            if (index >= _tools.Length)
            {
                return $"'{name}', which the browser server lists and this BrowserAI does not";
            }

            if (their.SequenceEqual(_tools[index]))
            {
                continue;
            }

            return string.Equals(name, _names[index], StringComparison.Ordinal)
                ? $"'{name}', whose definition differs"
                : $"'{_names[index]}', where the browser server lists '{name}'";
        }

        // ⚠️ Corrected 2026-10-10, the texts polish, page #52 (previously "the list around
        // the tools, which hold the same bytes"): where the difference is, and that the
        // tools are the same.
        return "in the answer outside the tools, which are the same";
    }

    /// <summary>
    /// Asks a session's child for its tools, and refuses the session when its answer
    /// is not this list byte for byte.
    /// </summary>
    /// <remarks>
    /// <b>Right after the child's handshake and before any call</b>, so no page
    /// exists and none of a page's own tools can be in the answer.
    /// </remarks>
    /// <param name="child">The child, its handshake done and no call made.</param>
    /// <param name="session">The session directory, for the refusal.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>A task that completes when the child's list is this list.</returns>
    /// <exception cref="InstallIsBrokenException">The child lists different tools.</exception>
    /// <exception cref="InvalidOperationException">The child did not answer with a result.</exception>
    public async Task CheckSessionChildAsync(ChildConnection child, string session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(session);

        var answer = await child.AskAsync("tools/list", new JsonObject(), cancellationToken).ConfigureAwait(false);

        if (answer.Response is null || answer.Payload is not { IsError: false } payload)
        {
            var why = answer.ProtocolFailure?.Message ?? answer.TransportFailure?.Message ?? "it wrote no result BrowserAI could read.";

            throw new InvalidOperationException($"The browser server did not answer tools/list after its handshake: {why}");
        }

        if (FirstDifference(payload.Json) is { } difference)
        {
            throw new InstallIsBrokenException(SessionErrors.InstallIsBroken(session, difference)) { Difference = difference };
        }
    }

    private static UpstreamToolList FromResource()
    {
        using var stream = typeof(UpstreamToolList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"This BrowserAI was built without its tool list: the resource '{ResourceName}' is not in the binary.");

        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);

        return Parse(bytes.ToArray(), ResourceName);
    }

    /// <summary>Where the root's <c>tools</c> array starts and ends, or <see langword="null"/>.</summary>
    private static (int Start, int End)? ToolsValue(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);

            if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
            {
                var isTools = reader.ValueTextEquals(ToolsMember);

                _ = reader.Read();

                if (isTools && reader.TokenType is JsonTokenType.StartArray)
                {
                    var start = (int)reader.TokenStartIndex;
                    reader.Skip();

                    return (start, (int)reader.BytesConsumed);
                }

                reader.Skip();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Each element's range within an array's bytes.</summary>
    private static List<(int Start, int End)> Elements(ReadOnlySpan<byte> array)
    {
        var ranges = new List<(int Start, int End)>();
        var reader = new Utf8JsonReader(array);

        _ = reader.Read();

        while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
        {
            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            ranges.Add((start, (int)reader.BytesConsumed));
        }

        return ranges;
    }

    /// <summary>A tool's <c>name</c>, read from its bytes, or <see langword="null"/>.</summary>
    private static string? NameIn(ReadOnlySpan<byte> tool)
    {
        try
        {
            var reader = new Utf8JsonReader(tool);

            if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
            {
                var isName = reader.ValueTextEquals(NameMember);

                _ = reader.Read();

                if (isName && reader.TokenType is JsonTokenType.String)
                {
                    return reader.GetString();
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// The bytes with every whitespace byte outside a string taken out; every byte
    /// inside a string, escapes included, kept as it was.
    /// </summary>
    private static byte[] Compact(ReadOnlySpan<byte> json)
    {
        var output = new List<byte>(json.Length);
        var inString = false;
        var escaped = false;

        foreach (var value in json)
        {
            if (inString)
            {
                output.Add(value);

                if (escaped)
                {
                    escaped = false;
                }
                else if (value is (byte)'\\')
                {
                    escaped = true;
                }
                else if (value is (byte)'"')
                {
                    inString = false;
                }
            }
            else if (value is (byte)'"')
            {
                inString = true;
                output.Add(value);
            }
            else if (value is not ((byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r'))
            {
                output.Add(value);
            }
        }

        return [.. output];
    }
}

/// <summary>
/// A session's child lists tools that differ from the list this binary was built
/// with, so the install is broken and the session does not open.
/// </summary>
/// <remarks>
/// The message is the refusal itself, <see cref="SessionErrors.InstallIsBroken"/>,
/// which is what <c>SessionManager</c> answers the caller with.
/// </remarks>
internal sealed class InstallIsBrokenException : Exception
{
    /// <summary>
    /// The first difference, as <see cref="UpstreamToolList.FirstDifference"/> words it,
    /// for the background's notice (10 b, 2026-10-10); <see langword="null"/> when the
    /// thrower named none.
    /// </summary>
    public string? Difference { get; init; }

    /// <summary>Creates one carrying the refusal.</summary>
    /// <param name="message">The refusal.</param>
    public InstallIsBrokenException(string message)
        : base(message)
    {
    }

    /// <summary>Creates one with a generic message.</summary>
    public InstallIsBrokenException()
        : base("A session's browser server lists tools that differ from the list this BrowserAI was built with.")
    {
    }

    /// <summary>Creates one carrying the refusal and its cause.</summary>
    /// <param name="message">The refusal.</param>
    /// <param name="innerException">The cause.</param>
    public InstallIsBrokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

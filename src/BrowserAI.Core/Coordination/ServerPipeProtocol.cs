// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BrowserAI.Coordination;

/// <summary>
/// What one server's pipe is called, what may be asked of it, and how an
/// answer is framed -- the half both ends of the pipe read.
/// </summary>
/// <remarks>
/// <para>
/// <b>The census entry is the address.</b> A server's pipe is named after its
/// live marker: <c>\\.\pipe\BrowserAI-</c> and the marker's file name without
/// its extension, which is the server's pid and a GUID. Whoever holds the list
/// of markers therefore holds the list of pipes, and a marker that is not held
/// names a pipe nobody is serving. Q284 a, the maintainer's words verbatim:
/// <i>"Q284 a"</i>.
/// </para>
/// <para>
/// <b>One request per connection, one line in, one framed answer out.</b> The
/// request is a verb and a newline. The answer is four bytes of little-endian
/// length and then that many bytes of UTF-8 JSON. The length is what lets a
/// client tell a whole answer from a server that died half-way through one:
/// measured 2026-09-24, three runs of a server terminated after writing half
/// its answer were each read back as <i>no answer</i> and never as a short one.
/// </para>
/// <para>
/// <b>There is no checksum, and the reason is the medium.</b> A pipe hands the
/// reader bytes the writer wrote in the order it wrote them, from a snapshot
/// built in the writer's memory, so there is no in-place rewrite for a reader to
/// catch half-done. The same record written into a file and re-read measured 22
/// torn reads in 3,000,000 that parsed as valid JSON with wrong values; that is
/// the failure a checksum would have been for, and the pipe does not have it.
/// </para>
/// </remarks>
internal static class ServerPipeProtocol
{
    /// <summary>What every server pipe's name starts with.</summary>
    public const string NamePrefix = @"\\.\pipe\BrowserAI-";

    /// <summary>
    /// The version of this protocol a server speaks, carried in every
    /// description so a reader can tell which fields to expect.
    /// </summary>
    public const int Version = 1;

    /// <summary>The verb that asks a server to describe itself.</summary>
    public const string DescribeVerb = "describe";

    /// <summary>The verb that asks a server to stop gracefully.</summary>
    public const string StopVerb = "stop";

    /// <summary>How many bytes a request may take, newline included.</summary>
    public const int MaximumRequestBytes = 64;

    /// <summary>
    /// The largest answer a client will read. A length prefix above it is a
    /// broken or hostile server, not a description.
    /// </summary>
    public const int MaximumReplyBytes = 1024 * 1024;

    /// <summary>The size of the length prefix in front of every answer.</summary>
    public const int LengthPrefixBytes = 4;

    /// <summary>
    /// How long a client gives one call, from connect to the last byte of the
    /// answer: <b>500 ms</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Derived from the measured cost of the call, 2026-09-24.</b> Over 100
    /// stand-in servers, one raw-pipe describe cost 208.7 µs at p50 and 387.2 µs
    /// at p99 walked one after another, and 258.8 µs at p50 and 1,989.9 µs at
    /// p99 with eight in flight at once. The worst of those is under 2 ms, so
    /// 500 ms is more than 250 times the slowest percentile measured: a server
    /// that has not answered by then is not a slow one.
    /// </para>
    /// <para>
    /// <b>And it is the whole of what one hung server can cost a caller.</b> A
    /// listener that stops answering holds its pipe open while its process is
    /// alive, so a caller has no other way to tell it from a busy one; the bound
    /// is what turns it into <i>no answer</i>. A server that has gone costs
    /// nothing like it, because its pipe has gone with it and the open fails at
    /// once -- which is why a caller reads the census first.
    /// </para>
    /// </remarks>
    public static TimeSpan CallBound { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The pipe name a server with this live marker listens on.</summary>
    /// <param name="markerPath">The marker file, <c>&lt;pid&gt;-&lt;guid&gt;.live</c>.</param>
    /// <returns>The full pipe name.</returns>
    public static string NameFor(string markerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        return NamePrefix + Path.GetFileNameWithoutExtension(markerPath);
    }

    /// <summary>The pid a live marker's name carries.</summary>
    /// <param name="markerPath">The marker file.</param>
    /// <param name="processId">The pid, when the name carries one.</param>
    /// <returns>Whether the name had the <c>&lt;pid&gt;-&lt;guid&gt;</c> shape.</returns>
    public static bool TryProcessIdOf(string markerPath, out int processId)
    {
        processId = 0;

        if (string.IsNullOrWhiteSpace(markerPath))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(markerPath);
        var dash = stem.IndexOf('-', StringComparison.Ordinal);

        return dash > 0
            && int.TryParse(stem.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out processId)
            && processId > 0;
    }

    /// <summary>The request a verb names, or <see langword="null"/> for one this build does not know.</summary>
    /// <param name="verb">The request line, without its newline.</param>
    /// <returns>The request.</returns>
    public static ServerPipeRequest? Parse(string verb) => verb switch
    {
        DescribeVerb => ServerPipeRequest.Describe,
        StopVerb => ServerPipeRequest.Stop,
        _ => null,
    };

    /// <summary>The bytes a client sends for a request.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The verb and its newline, in ASCII.</returns>
    public static byte[] Request(ServerPipeRequest request) =>
        Encoding.ASCII.GetBytes((request is ServerPipeRequest.Stop ? StopVerb : DescribeVerb) + "\n");

    /// <summary>Puts the length prefix in front of an answer.</summary>
    /// <param name="body">The answer's UTF-8 JSON.</param>
    /// <returns>The prefix and the body, in one buffer.</returns>
    public static byte[] Frame(ReadOnlySpan<byte> body)
    {
        var framed = new byte[LengthPrefixBytes + body.Length];

        BinaryPrimitives.WriteInt32LittleEndian(framed, body.Length);
        body.CopyTo(framed.AsSpan(LengthPrefixBytes));

        return framed;
    }

    /// <summary>The answer a server gives to <c>stop</c> before it acts on it.</summary>
    /// <param name="processId">The pid of the server that will stop.</param>
    /// <returns>The answer's JSON.</returns>
    public static byte[] Acknowledged(int processId) =>
        Json(writer =>
        {
            writer.WriteString(Fields.Answer, StopVerb);
            writer.WriteNumber(Fields.Protocol, Version);
            writer.WriteNumber(Fields.ProcessId, processId);
        });

    /// <summary>The answer to a request a server will not act on.</summary>
    /// <remarks>
    /// <b>The one refusal shape, so that a new reason to refuse is a new
    /// sentence and not a new answer.</b> A verb this build does not know is
    /// answered this way today; a server that one day declines a known verb for
    /// a reason of its own answers with the same shape and a different
    /// <c>why</c>, and every client already reads it.
    /// </remarks>
    /// <param name="request">What was asked, as it arrived.</param>
    /// <param name="why">Why it was refused.</param>
    /// <returns>The answer's JSON.</returns>
    public static byte[] Refused(string request, string why) =>
        Json(writer =>
        {
            writer.WriteString(Fields.Answer, Answers.Refused);
            writer.WriteNumber(Fields.Protocol, Version);
            writer.WriteString(Fields.Request, request);
            writer.WriteString(Fields.Why, why);
        });

    /// <summary>Writes one JSON object with no serializer.</summary>
    /// <param name="members">Writes the object's members.</param>
    /// <returns>The UTF-8 bytes.</returns>
    public static byte[] Json(Action<Utf8JsonWriter> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            members(writer);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The kinds of answer, as the <c>answer</c> member spells them.</summary>
    public static class Answers
    {
        /// <summary>A description.</summary>
        public const string Describe = DescribeVerb;

        /// <summary>A stop, acknowledged before it is acted on.</summary>
        public const string Stop = StopVerb;

        /// <summary>A request the server will not act on.</summary>
        public const string Refused = "refused";
    }

    /// <summary>The member names every answer uses, spelled once.</summary>
    public static class Fields
    {
        /// <summary>Which kind of answer this is.</summary>
        public const string Answer = "answer";

        /// <summary>The protocol version the server speaks.</summary>
        public const string Protocol = "protocol";

        /// <summary>The server's pid.</summary>
        public const string ProcessId = "pid";

        /// <summary>What a refused request asked for.</summary>
        public const string Request = "request";

        /// <summary>Why a request was refused.</summary>
        public const string Why = "why";
    }
}

/// <summary>What a client may ask a server's pipe.</summary>
internal enum ServerPipeRequest
{
    /// <summary>Say who you are, who you serve and what you hold.</summary>
    Describe,

    /// <summary>Acknowledge, then stop the way a client leaving stops you.</summary>
    Stop,
}

/// <summary>One answer, and what the server does once it has been read.</summary>
/// <param name="Body">The answer's JSON, before framing.</param>
/// <param name="AfterDelivery">
/// Run once the client has read the whole answer and closed its end, or
/// <see langword="null"/>. It is how <c>stop</c> acknowledges first and acts
/// second.
/// </param>
internal sealed record ServerPipeReply(byte[] Body, Action? AfterDelivery = null);

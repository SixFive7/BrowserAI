// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review scratch prototype, 2026-09-24. Not product code.
// The per-server record a coordinator would read, and the four on-disk framings measured:
//   none  - JSON padded with spaces, nothing else (what "no checksum" means)
//   crc   - a fixed-width header "BAI1 crc32=XXXXXXXX len=NNNNNNNN\n" then the JSON, padded
//   seq   - a fixed-width "seq=XXXXXXXXXXXXXXXX\n" line, then the crc framing (crc used only to AUDIT, never to accept)
//   lock  - the crc framing, written and read under a LockFileEx gate on byte long.MaxValue (crc used only to audit)
//   posix - a sidecar file holding the crc framing, replaced by a POSIX-semantics rename (crc used only to audit)
// Every changing field is a pure function of the write counter n, so a reader can tell a torn-but-parseable
// record (fields from two writes) from a whole one without trusting any checksum.
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

internal static class Record
{
    public const int HeaderLength = 33; // "BAI1 crc32=XXXXXXXX len=NNNNNNNN\n"
    public const int SeqLength = 21;    // "seq=XXXXXXXXXXXXXXXX\n"
    public static readonly DateTime Epoch = new(2026, 9, 24, 11, 0, 0, DateTimeKind.Utc);

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[i] = c;
        }

        return table;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    public static DateTime LastCallFor(long n) => Epoch.AddMilliseconds(n * 37);

    /// <summary>The JSON a server would publish, write number n.</summary>
    public static byte[] Json(long n, int pid, int sessions, string project)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("schema", 1);
            w.WriteString("kind", "server");
            w.WriteNumber("pid", pid);
            w.WriteNumber("created", 134347231732354603L);
            w.WriteString("image", @"C:\Users\jori\AppData\Local\BrowserAI.app\current\BrowserAI.Server.exe");
            w.WriteString("version", "1.1.0");
            w.WriteStartObject("client");
            w.WriteString("name", "claude-code");
            w.WriteString("title", "Claude Code");
            w.WriteString("version", "2.1.281");
            w.WriteEndObject();
            w.WriteString("project", project);
            w.WriteString("started", Epoch.ToString("O", CultureInfo.InvariantCulture));
            w.WriteNumber("n", n);
            w.WriteString("lastCall", LastCallFor(n).ToString("O", CultureInfo.InvariantCulture));
            w.WriteNumber("inFlight", n % 2);
            w.WriteNumber("calls", n);
            w.WriteStartArray("sessions");
            for (var i = 0; i < sessions; i++)
            {
                w.WriteStartObject();
                w.WriteString("dir", $@"C:\Source\SixFive7\BrowserAI\.work\sessions\research-session-{i:D2}");
                w.WriteString("purpose", $"Session {i}: reads the Velopack release notes and the GitHub releases page to compare what shipped in each version.");
                w.WriteBoolean("browser", i % 2 == 0);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The crc framing: header then JSON.</summary>
    public static byte[] Framed(byte[] json)
    {
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"BAI1 crc32={Crc32(json):X8} len={json.Length:D8}\n"));
        if (header.Length != HeaderLength)
        {
            throw new InvalidOperationException("header width");
        }

        var all = new byte[header.Length + json.Length];
        header.CopyTo(all, 0);
        json.CopyTo(all, header.Length);
        return all;
    }

    public static byte[] SeqLine(ulong seq) =>
        Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"seq={seq:X16}\n"));

    public static bool TryParseSeq(ReadOnlySpan<byte> line, out ulong seq)
    {
        seq = 0;
        if (line.Length < SeqLength || line[0] != (byte)'s' || line[SeqLength - 1] != (byte)'\n')
        {
            return false;
        }

        return ulong.TryParse(Encoding.ASCII.GetString(line.Slice(4, 16)), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seq);
    }

    /// <summary>Splits a crc framing. Answers the JSON slice and whether the crc matched.</summary>
    public static bool TryUnframe(ReadOnlySpan<byte> region, out ReadOnlySpan<byte> json, out bool crcOk)
    {
        json = default;
        crcOk = false;
        if (region.Length < HeaderLength || region[0] != (byte)'B' || region[HeaderLength - 1] != (byte)'\n')
        {
            return false;
        }

        var header = Encoding.ASCII.GetString(region[..HeaderLength]);
        if (!uint.TryParse(header.AsSpan(11, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var crc)
            || !int.TryParse(header.AsSpan(24, 8), NumberStyles.None, CultureInfo.InvariantCulture, out var len)
            || len <= 0 || HeaderLength + len > region.Length)
        {
            return false;
        }

        json = region.Slice(HeaderLength, len);
        crcOk = Crc32(json) == crc;
        return true;
    }

    /// <summary>What a record claims, and whether its changing fields agree with each other.</summary>
    public enum Consistency
    {
        Whole,          // parsed, and every changing field is the one write n says it is
        Mixed,          // parsed as JSON, but the fields come from more than one write: SILENTLY WRONG if accepted
        Unparseable,    // not JSON: a parser would have caught it
    }

    public static Consistency Check(ReadOnlySpan<byte> json, out long n)
    {
        n = -1;
        try
        {
            var reader = new Utf8JsonReader(json);
            long? nn = null, calls = null, inFlight = null;
            string? lastCall = null;
            var depth = 0;
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    depth++;
                }
                else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                {
                    depth--;
                }
                else if (reader.TokenType == JsonTokenType.PropertyName && depth == 1)
                {
                    var name = reader.GetString();
                    reader.Read();
                    switch (name)
                    {
                        case "n": nn = reader.GetInt64(); break;
                        case "calls": calls = reader.GetInt64(); break;
                        case "inFlight": inFlight = reader.GetInt64(); break;
                        case "lastCall": lastCall = reader.GetString(); break;
                        default:
                            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                            {
                                reader.Skip();
                            }

                            break;
                    }
                }
            }

            if (nn is null || calls is null || inFlight is null || lastCall is null)
            {
                return Consistency.Unparseable;
            }

            n = nn.Value;
            var expected = LastCallFor(n).ToString("O", CultureInfo.InvariantCulture);
            return calls == n && inFlight == n % 2 && lastCall == expected ? Consistency.Whole : Consistency.Mixed;
        }
        catch (JsonException)
        {
            return Consistency.Unparseable;
        }
        catch (InvalidOperationException)
        {
            return Consistency.Unparseable;
        }
    }

    /// <summary>The "none" framing: the whole region is the JSON plus trailing spaces.</summary>
    public static ReadOnlySpan<byte> TrimPadding(ReadOnlySpan<byte> region)
    {
        var end = region.Length;
        while (end > 0 && (region[end - 1] == (byte)' ' || region[end - 1] == 0))
        {
            end--;
        }

        return region[..end];
    }
}

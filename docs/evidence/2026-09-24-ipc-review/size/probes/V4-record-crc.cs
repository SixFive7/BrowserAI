// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review size probe: a record rewritten in place inside an open file, framed with a hand-written CRC32.
namespace BrowserAI;

internal static class IpcProbe
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            t[i] = c;
        }

        return t;
    }

    private static uint Crc(System.ReadOnlySpan<byte> d)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in d)
        {
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return ~c;
    }

    public static void Start()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "probe-" + System.Environment.ProcessId + ".live");
        var held = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.ReadWrite, System.IO.FileShare.Read, 1);
        var json = System.Text.Encoding.UTF8.GetBytes("{\"pid\":" + System.Environment.ProcessId + "}");
        var header = System.Text.Encoding.ASCII.GetBytes("BAI1 crc32=" + Crc(json).ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + " len=" + json.Length.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + "\n");
        var block = new byte[4096];
        header.CopyTo(block, 0);
        json.CopyTo(block, header.Length);
        System.IO.RandomAccess.Write(held.SafeFileHandle, block, 0);
    }
}

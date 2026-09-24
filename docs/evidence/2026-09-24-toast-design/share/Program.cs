// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Q254 scratch: how a reader reads a live marker another process holds open, measured.
// The holder opens EXACTLY as LiveInstances.Join does (src/BrowserAI.Core/Updates/LiveInstances.cs:314):
// FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize 1.
using System.Diagnostics;
using System.Text;

internal static class Program
{
    private const int Block = 4096;

    private static int Main(string[] args)
    {
        return args[0] switch
        {
            "holder" => Holder(args[1], args[2]),
            "experiments" => Experiments(args[1]),
            _ => 2,
        };
    }

    // ---------------- the holder, in its own process ----------------
    private static int Holder(string path, string mode)
    {
        using var held = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
        var stop = path + ".stop";
        long seq = 0;
        WriteRecord(held, seq, fixedSize: true);
        File.WriteAllText(path + ".ready", "1");

        while (!File.Exists(stop))
        {
            switch (mode)
            {
                case "static":
                    Thread.Sleep(20);
                    break;
                case "rewrite-fixed":
                    WriteRecord(held, ++seq, fixedSize: true);
                    break;
                case "rewrite-var":
                    WriteRecord(held, ++seq, fixedSize: false);
                    break;
                case "touch":
                    // Rewriting the same bytes moves nothing but the timestamp.
                    WriteRecord(held, seq, fixedSize: true);
                    Thread.Sleep(200);
                    break;
            }
        }

        File.WriteAllText(path + ".seq", seq.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return 0;
    }

    // One record: a JSON object whose last member is a checksum of everything before it,
    // so a torn read is DETECTED and never parsed as something else.
    private static void WriteRecord(FileStream held, long seq, bool fixedSize)
    {
        var pad = new string('x', (int)(seq % 7) * 97);
        var body = "{\"schema\":1,\"pid\":" + Environment.ProcessId + ",\"seq\":" + seq
            + ",\"client\":\"claude-code\",\"lastCallUtc\":\"" + DateTime.UtcNow.ToString("O") + "\",\"pad\":\"" + pad + "\"";
        var sum = Checksum(body);
        var text = body + ",\"sum\":" + sum + "}\n";
        var bytes = Encoding.UTF8.GetBytes(text);

        if (fixedSize)
        {
            var buffer = new byte[Block];
            Array.Fill(buffer, (byte)0x20);
            bytes.CopyTo(buffer, 0);
            held.Position = 0;
            held.Write(buffer);   // one WriteFile of the whole block; the length never changes
        }
        else
        {
            held.SetLength(0);    // the window this mode exists to measure
            held.Position = 0;
            held.Write(bytes);
        }
    }

    private static uint Checksum(string s)
    {
        uint h = 2166136261;
        foreach (var c in s)
        {
            h ^= c;
            h *= 16777619;
        }

        return h;
    }

    // Parses a record; answers null for a torn read.
    private static long? Parse(string text)
    {
        text = text.TrimEnd((char)0x20, '\n', '\0');
        var at = text.LastIndexOf(",\"sum\":", StringComparison.Ordinal);
        if (at < 0 || !text.EndsWith('}'))
        {
            return null;
        }

        var body = text[..at];
        if (!uint.TryParse(text[(at + 7)..^1], out var sum) || sum != Checksum(body))
        {
            return null;
        }

        var s = body.IndexOf("\"seq\":", StringComparison.Ordinal) + 6;
        var e = body.IndexOf(',', s);
        return long.Parse(body[s..e], System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------------- the experiments ----------------
    private static int Experiments(string dir)
    {
        Directory.CreateDirectory(dir);
        var self = Environment.ProcessPath!;

        // E1: which reader opens succeed against a live holder.
        var marker = Path.Combine(dir, "e1.live");
        using (StartHolder(self, marker, "static"))
        {
            Report("E1 File.ReadAllText (Read, share Read)", () => File.ReadAllText(marker).Length);
            Report("E1 FileStream(Read, share ReadWrite)", () => ReadWith(marker, FileShare.ReadWrite).Length);
            Report("E1 FileStream(Read, share ReadWrite|Delete)", () => ReadWith(marker, FileShare.ReadWrite | FileShare.Delete).Length);
            Report("E1 census probe (ReadWrite, share Read)", () => { using var p = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1); return 0; });
            Report("E1 read-only held-ness probe (Read, share Read)", () => { using var p = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read, 1); return 0; });
            Report("E1 content parses, seq", () => Parse(ReadWith(marker, FileShare.ReadWrite | FileShare.Delete)) ?? -1);

            // E2: a reader holding the file open does not change the census answer while the holder lives.
            using var reader = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            Report("E2 census probe while a share-RW|Delete reader is open, holder alive", () => { using var p = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1); return 0; });
            Stop(marker);
        }

        // E3: after the holder dies, with a reader still open: can the census open it, can the reclaim delete it?
        foreach (var share in new[] { FileShare.ReadWrite | FileShare.Delete, FileShare.ReadWrite })
        {
            var m = Path.Combine(dir, "e3-" + (share.HasFlag(FileShare.Delete) ? "del" : "nodel") + ".live");
            var h = StartHolder(self, m, "static");
            using var reader = new FileStream(m, FileMode.Open, FileAccess.Read, share, 1);
            Stop(m);
            h.Dispose();
            Report($"E3 [{share}] census probe after holder exit, reader open", () => { using var p = new FileStream(m, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1); return 0; });
            Report($"E3 [{share}] File.Delete after holder exit, reader open", () => { File.Delete(m); return 0; });
            Report($"E3 [{share}] name still listed after that delete", () => Directory.EnumerateFiles(dir, Path.GetFileName(m)).Count());
        }

        // E4: torn reads under continuous rewriting, both write shapes, 20,000 reads each.
        foreach (var mode in new[] { "rewrite-fixed", "rewrite-var" })
        {
            var m = Path.Combine(dir, "e4-" + mode + ".live");
            using (StartHolder(self, m, mode))
            {
                int ok = 0, torn = 0, empty = 0;
                var clock = Stopwatch.StartNew();
                for (var i = 0; i < 20000; i++)
                {
                    var text = ReadWith(m, FileShare.ReadWrite | FileShare.Delete);
                    if (text.Trim((char)0x20, '\n', '\0').Length == 0)
                    {
                        empty++;
                        continue;
                    }

                    if (Parse(text) is null)
                    {
                        torn++;
                    }
                    else
                    {
                        ok++;
                    }
                }

                var elapsed = clock.Elapsed;
                Stop(m);
                Console.WriteLine($"E4 {mode}: reads=20000 ok={ok} torn={torn} empty={empty} in {elapsed.TotalMilliseconds:F0} ms ({elapsed.TotalMilliseconds * 1000 / 20000:F1} us/read)");
            }

            Console.WriteLine($"E4 {mode}: the holder wrote {File.ReadAllText(m + ".seq")} records during the reads");
        }

        // E5: does a reader see the holder's last-write time move, and through which instrument?
        {
            var m = Path.Combine(dir, "e5.live");
            using (StartHolder(self, m, "touch"))
            {
                var first = Times(dir, m);
                Thread.Sleep(1500);
                var second = Times(dir, m);
                var now = DateTime.UtcNow;
                Console.WriteLine("E5 after 1.5 s of rewrites every 200 ms:");
                Console.WriteLine($"   FileInfo (GetFileAttributesEx)     moved {(second.Attr - first.Attr).TotalMilliseconds:F0} ms, age {(now - second.Attr).TotalMilliseconds:F0} ms");
                Console.WriteLine($"   directory enumeration (FindFirst)  moved {(second.Find - first.Find).TotalMilliseconds:F0} ms, age {(now - second.Find).TotalMilliseconds:F0} ms");
                Console.WriteLine($"   by handle                          moved {(second.Handle - first.Handle).TotalMilliseconds:F0} ms, age {(now - second.Handle).TotalMilliseconds:F0} ms");
                Stop(m);
            }
        }

        // E6: a sidecar replaced by temp + File.Move(overwrite) while a reader holds it, both share modes.
        foreach (var share in new[] { FileShare.ReadWrite | FileShare.Delete, FileShare.ReadWrite })
        {
            var target = Path.Combine(dir, "e6-" + (share.HasFlag(FileShare.Delete) ? "del" : "nodel") + ".json");
            File.WriteAllText(target, "{\"v\":1}");
            using var reader = new FileStream(target, FileMode.Open, FileAccess.Read, share, 1);
            var temp = target + ".new";
            File.WriteAllText(temp, "{\"v\":2}");
            Report($"E6 [{share}] File.Move(temp, target, overwrite) while a reader holds target", () => { File.Move(temp, target, overwrite: true); return 0; });
        }

        return 0;
    }

    private static (DateTime Attr, DateTime Find, DateTime Handle) Times(string dir, string path)
    {
        var attr = new FileInfo(path).LastWriteTimeUtc;
        var find = new DirectoryInfo(dir).EnumerateFiles(Path.GetFileName(path)).Single().LastWriteTimeUtc;
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
        var handle = File.GetLastWriteTimeUtc(s.SafeFileHandle);
        return (attr, find, handle);
    }

    private static string ReadWith(string path, FileShare share)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, share, bufferSize: 1);
        var buffer = new byte[Block * 2];
        var n = 0;
        int r;
        while ((r = s.Read(buffer, n, buffer.Length - n)) > 0)
        {
            n += r;
        }

        return Encoding.UTF8.GetString(buffer, 0, n);
    }

    private static void Report(string what, Func<long> action)
    {
        try
        {
            Console.WriteLine($"{what}: OK ({action()})");
        }
        catch (Exception e)
        {
            Console.WriteLine($"{what}: {e.GetType().Name} hr=0x{e.HResult:X8} ({e.Message.Split('\n')[0].Trim()})");
        }
    }

    private sealed class HolderProcess(Process p) : IDisposable
    {
        public void Dispose()
        {
            if (!p.WaitForExit(10000))
            {
                p.Kill();
            }

            p.WaitForExit();
            p.Dispose();
        }
    }

    private static HolderProcess StartHolder(string self, string path, string mode)
    {
        foreach (var f in new[] { path, path + ".ready", path + ".stop", path + ".seq" })
        {
            if (File.Exists(f))
            {
                File.Delete(f);
            }
        }

        var psi = new ProcessStartInfo(self) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("holder");
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add(mode);
        var p = Process.Start(psi)!;
        while (!File.Exists(path + ".ready"))
        {
            Thread.Sleep(10);
        }

        return new HolderProcess(p);
    }

    private static void Stop(string path) => File.WriteAllText(path + ".stop", "1");
}

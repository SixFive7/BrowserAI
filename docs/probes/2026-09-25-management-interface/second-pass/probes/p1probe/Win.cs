// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Window facts read from the probe's own desktop: enumeration by owning pid,
// PrintWindow captures, the window icon, and the taskbar identity properties.
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

internal static unsafe class Win
{
    public sealed record Top(nint Handle, uint ProcessId, string Class, string Title, bool Visible, W.RECT Rect, long Style, long ExStyle, nint Owner);

    public static List<Top> TopLevel()
    {
        var found = new List<Top>();
        W.EnumWindowsProc callback = (h, unused) =>
        {
            _ = W.GetWindowRect(h, out var r);
            found.Add(new Top(h, W.PidOf(h), W.ClassOf(h), W.TitleOf(h), W.IsWindowVisible(h), r, W.GetWindowLongPtrW(h, -16), W.GetWindowLongPtrW(h, -20), W.GetWindow(h, 4 /* GW_OWNER */)));
            return true;
        };
        _ = W.EnumWindows(callback, 0);
        GC.KeepAlive(callback);
        return found;
    }

    /// <summary>Message-only windows of one class, the walk BrowserAI's stray sweep makes.</summary>
    public static List<(nint Handle, uint ProcessId, string Title)> MessageWindows(string className)
    {
        var found = new List<(nint, uint, string)>();
        nint after = 0;
        for (var guard = 0; guard < 10000; guard++)
        {
            after = W.FindWindowExW(-3 /* HWND_MESSAGE */, after, className, null);
            if (after == 0)
            {
                break;
            }

            found.Add((after, W.PidOf(after), W.TitleOf(after)));
        }

        return found;
    }

    public static string Capture(nint window, string png)
    {
        _ = W.GetWindowRect(window, out var wr);
        var frame = wr;
        if (W.DwmGetWindowAttribute(window, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var fr, sizeof(W.RECT)) == 0)
        {
            frame = fr;
        }

        var w = wr.Right - wr.Left;
        var h = wr.Bottom - wr.Top;
        if (w <= 0 || h <= 0)
        {
            return $"FAILED: empty rect {wr}";
        }

        foreach (var flags in new uint[] { 2 /* PW_RENDERFULLCONTENT */, 0 })
        {
            var pixels = Print(window, w, h, flags, out var ok);
            if (!ok)
            {
                continue;
            }

            if (IsBlank(pixels))
            {
                continue;
            }

            var x0 = Math.Max(0, frame.Left - wr.Left);
            var y0 = Math.Max(0, frame.Top - wr.Top);
            var cw = Math.Min(w - x0, frame.Right - frame.Left);
            var ch = Math.Min(h - y0, frame.Bottom - frame.Top);
            var rgb = new byte[cw * ch * 3];
            for (var y = 0; y < ch; y++)
            {
                for (var x = 0; x < cw; x++)
                {
                    var s = (((y + y0) * w) + x + x0) * 4;
                    var d = ((y * cw) + x) * 3;
                    rgb[d] = pixels[s + 2];
                    rgb[d + 1] = pixels[s + 1];
                    rgb[d + 2] = pixels[s];
                }
            }

            Png.Write(png, cw, ch, rgb);
            return $"ok flags={flags} window={w}x{h} frame={cw}x{ch} dpi={W.GetDpiForWindow(window)}";
        }

        return "FAILED: every PrintWindow variant was blank or refused";
    }

    private static byte[] Print(nint window, int w, int h, uint flags, out bool ok)
    {
        var mem = W.CreateCompatibleDC(0);
        var bi = new W.BITMAPINFOHEADER { biSize = sizeof(W.BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bmp = W.CreateDIBSection(mem, ref bi, 0, out var bits, 0, 0);
        var old = W.SelectObject(mem, bmp);
        ok = W.PrintWindow(window, mem, flags);
        _ = W.GdiFlush();
        var buffer = new byte[w * h * 4];
        Marshal.Copy(bits, buffer, 0, buffer.Length);
        _ = W.SelectObject(mem, old);
        _ = W.DeleteObject(bmp);
        _ = W.DeleteDC(mem);
        return buffer;
    }

    private static bool IsBlank(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The icon a window reports, drawn 64x64 on white, as a PNG.</summary>
    public static string DumpIcon(nint window, string png)
    {
        var source = "WM_GETICON(ICON_BIG)";
        _ = W.SendMessageTimeoutW(window, 0x007F /* WM_GETICON */, 1, 0, 0x0002, 2000, out var icon);
        if (icon == 0)
        {
            source = "WM_GETICON(ICON_SMALL2)";
            _ = W.SendMessageTimeoutW(window, 0x007F, 2, 0, 0x0002, 2000, out icon);
        }

        if (icon == 0)
        {
            source = "WM_GETICON(ICON_SMALL)";
            _ = W.SendMessageTimeoutW(window, 0x007F, 0, 0, 0x0002, 2000, out icon);
        }

        if (icon == 0)
        {
            source = "GCLP_HICON";
            icon = W.GetClassLongPtrW(window, -14);
        }

        if (icon == 0)
        {
            return "no icon";
        }

        const int size = 64;
        var mem = W.CreateCompatibleDC(0);
        var bi = new W.BITMAPINFOHEADER { biSize = sizeof(W.BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
        var bmp = W.CreateDIBSection(mem, ref bi, 0, out var bits, 0, 0);
        var old = W.SelectObject(mem, bmp);
        var white = new byte[size * size * 4];
        Array.Fill(white, (byte)255);
        Marshal.Copy(white, 0, bits, white.Length);
        var drawn = W.DrawIconEx(mem, 0, 0, icon, size, size, 0, 0, 3 /* DI_NORMAL */);
        _ = W.GdiFlush();
        var pixels = new byte[size * size * 4];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        _ = W.SelectObject(mem, old);
        _ = W.DeleteObject(bmp);
        _ = W.DeleteDC(mem);

        var rgb = new byte[size * size * 3];
        for (var i = 0; i < size * size; i++)
        {
            rgb[i * 3] = pixels[(i * 4) + 2];
            rgb[(i * 3) + 1] = pixels[(i * 4) + 1];
            rgb[(i * 3) + 2] = pixels[i * 4];
        }

        Png.Write(png, size, size, rgb);
        return $"{source} handle=0x{icon:X} drawn={drawn}";
    }

    private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    /// <summary>PKEY_AppUserModel_ID (5), RelaunchCommand (2), RelaunchIconResource (3), RelaunchDisplayNameResource (4).</summary>
    public static string TaskbarIdentity(nint window, string? setIdTo = null)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"); // IPropertyStore
        var hr = W.SHGetPropertyStoreForWindow(window, ref iid, out var store);
        if (hr != 0 || store == 0)
        {
            return $"SHGetPropertyStoreForWindow hr=0x{hr:X8}";
        }

        var text = new StringBuilder();
        var vtable = *(nint**)store;
        var getValue = (delegate* unmanaged[Stdcall]<nint, W.PROPERTYKEY*, byte*, int>)vtable[5];
        var setValue = (delegate* unmanaged[Stdcall]<nint, W.PROPERTYKEY*, byte*, int>)vtable[6];
        var commit = (delegate* unmanaged[Stdcall]<nint, int>)vtable[7];
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtable[2];

        if (setIdTo is not null)
        {
            var key = new W.PROPERTYKEY { fmtid = AppUserModel, pid = 5 };
            var variant = stackalloc byte[24];
            new Span<byte>(variant, 24).Clear();
            var value = Marshal.StringToCoTaskMemUni(setIdTo);
            *(ushort*)variant = 31; // VT_LPWSTR
            *(nint*)(variant + 8) = value;
            var set = setValue(store, &key, variant);
            var committed = commit(store);
            Marshal.FreeCoTaskMem(value);
            text.Append($"SetValue(AppUserModel_ID={setIdTo}) hr=0x{set:X8} commit=0x{committed:X8}; ");
        }

        foreach (var (pid, label) in new[] { (5u, "AppUserModel_ID"), (2u, "RelaunchCommand"), (3u, "RelaunchIconResource"), (4u, "RelaunchDisplayNameResource") })
        {
            var key = new W.PROPERTYKEY { fmtid = AppUserModel, pid = pid };
            var variant = stackalloc byte[24];
            new Span<byte>(variant, 24).Clear();
            var got = getValue(store, &key, variant);
            var vt = *(ushort*)variant;
            var value = vt == 31 ? Marshal.PtrToStringUni(*(nint*)(variant + 8)) : vt == 0 ? "(empty)" : $"(vt {vt})";
            text.Append($"{label}=[{value}] hr=0x{got:X8}; ");
            _ = W.PropVariantClear((nint)variant);
        }

        _ = release(store);
        return text.ToString();
    }
}

internal static class Png
{
    private static readonly uint[] Table = BuildTable();

    public static void Write(string path, int width, int height, byte[] rgb)
    {
        using var file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        file.Write(Encode(width, height, rgb));
    }

    /// <summary>The chunks after the signature, so a caller can also embed the image.</summary>
    public static byte[] Bytes(int width, int height, byte[] rgb)
    {
        using var all = new MemoryStream();
        all.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        all.Write(Encode(width, height, rgb));
        return all.ToArray();
    }

    private static byte[] Encode(int width, int height, byte[] rgb)
    {
        using var output = new MemoryStream();
        var header = new byte[13];
        BigEndian(header, 0, (uint)width);
        BigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 2;
        Chunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgb, y * width * 3, width * 3);
            }
        }

        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var length = new byte[4];
        BigEndian(length, 0, (uint)data.Length);
        s.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var b in typeBytes)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        var tail = new byte[4];
        BigEndian(tail, 0, crc ^ 0xFFFFFFFFu);
        s.Write(tail);
    }

    private static void BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

/// <summary>
/// A proxy that lets nothing out: it records the first line of every request the
/// browser sends through it and answers 502, bound to 127.0.0.1 only.
/// </summary>
internal sealed class Sink : IDisposable
{
    private readonly System.Net.Sockets.Socket _listener;
    private readonly StreamWriter _log;
    private readonly System.Diagnostics.Stopwatch _clock;
    private readonly Lock _gate = new();

    public Sink(string logPath, System.Diagnostics.Stopwatch clock)
    {
        _clock = clock;
        _log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        _listener = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true,
        };
        _listener.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        _listener.Listen(64);
        Port = ((System.Net.IPEndPoint)_listener.LocalEndPoint!).Port;
        var thread = new Thread(Accept) { IsBackground = true, Name = "sink" };
        thread.Start();
    }

    public int Port { get; }

    public int Requests { get; private set; }

    private void Accept()
    {
        while (true)
        {
            System.Net.Sockets.Socket client;
            try
            {
                client = _listener.Accept();
            }
            catch (Exception)
            {
                return;
            }

            _ = ThreadPool.QueueUserWorkItem(_ => Serve(client));
        }
    }

    private void Serve(System.Net.Sockets.Socket client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 5000;
                var buffer = new byte[8192];
                var total = 0;
                while (total < buffer.Length)
                {
                    var n = client.Receive(buffer, total, buffer.Length - total, System.Net.Sockets.SocketFlags.None);
                    if (n <= 0)
                    {
                        break;
                    }

                    total += n;
                    if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                var head = Encoding.ASCII.GetString(buffer, 0, total);
                var first = head.Split("\r\n")[0];
                lock (_gate)
                {
                    Requests++;
                    _log.WriteLine($"{_clock.Elapsed.TotalSeconds:F1}\t{first}");
                }

                _ = client.Send(Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        _log.Dispose();
    }
}

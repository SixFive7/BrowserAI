// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// p1probe: what option P1 would do, as a NativeAOT process.
//
// It starts Chromium itself (CreateProcessW, an exact inherited handle list),
// hands it the DevTools pipe through --remote-debugging-io-pipes, loads a page
// with no port (every request of a made-up https origin is answered over the
// pipe with Fetch.fulfillRequest), and talks to the page through a binding.
// No node, no Playwright, no socket.
//
//   p1probe <chrome.exe> <userDataDir> <outDir> <label> <headless|app> [key=value ...] [-- chrome flags ...]
//
//   hold=<ms>    keep the page open this long after it is ready (the driver samples memory)
//   idle=<ms>    then stay idle this long (network observation)
//   sink=1       run the 127.0.0.1 sink proxy, add --proxy-server and --log-net-log
//   second=1     start a second chrome on the same user data dir
//   close=cdp|wm|pipe   how the browser is asked to end
//   shots=1      PrintWindow and icon dumps of the browser's top-level windows
//   bare=1       leave out --no-first-run and --no-default-browser-check
//   aumid=1      try to set the window's AppUserModel ID from this process
//   navout=1     navigate the page to an outside URL at the end (must be blocked)
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

var clock = Stopwatch.StartNew();
var chrome = Path.GetFullPath(args[0]);
var userData = Path.GetFullPath(args[1]);
var outDir = Path.GetFullPath(args[2]);
var label = args[3];
var mode = args[4];
var options = new Dictionary<string, string>(StringComparer.Ordinal);
var extra = new List<string>();
var afterDashes = false;
foreach (var a in args[5..])
{
    if (afterDashes)
    {
        extra.Add(a);
    }
    else if (a == "--")
    {
        afterDashes = true;
    }
    else
    {
        var eq = a.IndexOf('=');
        options[a[..eq]] = a[(eq + 1)..];
    }
}

int Opt(string key, int fallback = 0) => options.TryGetValue(key, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
string OptS(string key, string fallback) => options.TryGetValue(key, out var v) ? v : fallback;

Directory.CreateDirectory(outDir);
var resultFile = Path.Combine(outDir, label + ".result.txt");
var readyFile = Path.Combine(outDir, label + ".ready");
File.Delete(readyFile);
var results = new List<string>();
var resultGate = new Lock();

void R(string key, string value)
{
    lock (resultGate)
    {
        results.Add($"{clock.Elapsed.TotalMilliseconds,9:F1}\t{key}\t{value}");
    }
}

void Flush()
{
    lock (resultGate)
    {
        File.WriteAllLines(resultFile, results);
    }
}

const string Origin = "https://app.browserai.invalid";
var iconPng = Png.Bytes(32, 32, IconPixels());
var iconData = "data:image/png;base64," + Convert.ToBase64String(iconPng);
var html = $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>BrowserAI</title><link rel=\"icon\" href=\"{iconData}\"><link rel=\"stylesheet\" href=\"/app.css\"><script src=\"/app.js\" defer></script></head><body><h1>BrowserAI</h1><p id=\"state\">loading</p></body></html>";
const string Css = "html{color-scheme:light dark;font:14px system-ui}body{margin:24px}";
const string Js = """
const send = (o) => window.browseraiHost(JSON.stringify(o));
window.hostPing = (n) => { send({ pong: n }); return n; };
document.getElementById('state').textContent = 'ready';
send({ ready: true, dark: matchMedia('(prefers-color-scheme: dark)').matches, inner: [innerWidth, innerHeight], outer: [outerWidth, outerHeight], dpr: devicePixelRatio, origin: location.origin, secure: isSecureContext, title: document.title, ua: navigator.userAgent });
""";
const string Csp = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

Sink? sink = null;
CdpPipe? cdp = null;
Chromium.Started? started = null;
var exitCode = 1;

try
{
    R("desktop", W.DesktopName());
    R("mode", mode);

    var flags = new List<string> { $"--user-data-dir={userData}" };
    if (Opt("bare") == 0)
    {
        flags.Add("--no-first-run");
        flags.Add("--no-default-browser-check");
    }

    if (mode == "headless")
    {
        flags.Add("--headless=new");
    }
    else
    {
        flags.Add("--app=data:text/html,<title>BrowserAI</title>");
        flags.Add("--window-size=980,720");
        flags.Add("--window-position=60,60");
    }

    if (Opt("sink") == 1)
    {
        sink = new Sink(Path.Combine(outDir, label + ".sink.tsv"), clock);
        flags.Add($"--proxy-server=http://127.0.0.1:{sink.Port}");
        flags.Add($"--log-net-log={Path.Combine(outDir, label + ".netlog.json")}");
        R("sink_port", sink.Port.ToString(CultureInfo.InvariantCulture));
    }

    flags.AddRange(extra);

    var launchAt = clock.Elapsed.TotalMilliseconds;
    started = Chromium.Launch(chrome, flags);
    R("launched", $"pid={started.Process.dwProcessId} in={started.ChildReadValue} out={started.ChildWriteValue}");
    R("command_line", started.CommandLine);

    cdp = new CdpPipe(started.WriteToBrowser, started.ReadFromBrowser, clock);

    string? session = null;
    var ready = new ManualResetEventSlim(false);
    string? readyPayload = null;
    var pongs = 0;
    var blocked = new List<string>();
    var served = new List<string>();
    var targetEvents = new List<string>();

    cdp.OnEvent = (method, p, sid) =>
    {
        switch (method)
        {
            case "Fetch.requestPaused":
                {
                    var requestId = p.GetProperty("requestId").GetString()!;
                    var url = p.GetProperty("request").GetProperty("url").GetString()!;
                    if (url.StartsWith(Origin + "/", StringComparison.Ordinal))
                    {
                        var path = url[Origin.Length..];
                        var (type, body) = path switch
                        {
                            "/" => ("text/html; charset=utf-8", html),
                            "/app.js" => ("text/javascript; charset=utf-8", Js),
                            "/app.css" => ("text/css; charset=utf-8", Css),
                            _ => (string.Empty, string.Empty),
                        };
                        lock (served)
                        {
                            served.Add(path);
                        }

                        if (type.Length == 0)
                        {
                            _ = cdp!.Post("Fetch.fulfillRequest", $"{{\"requestId\":{J.Str(requestId)},\"responseCode\":404}}", sid);
                        }
                        else
                        {
                            var headers = $"[{{\"name\":\"Content-Type\",\"value\":{J.Str(type)}}},{{\"name\":\"Content-Security-Policy\",\"value\":{J.Str(Csp)}}},{{\"name\":\"X-Content-Type-Options\",\"value\":\"nosniff\"}},{{\"name\":\"Cache-Control\",\"value\":\"no-store\"}}]";
                            _ = cdp!.Post("Fetch.fulfillRequest", $"{{\"requestId\":{J.Str(requestId)},\"responseCode\":200,\"responseHeaders\":{headers},\"body\":{J.Str(Convert.ToBase64String(Encoding.UTF8.GetBytes(body)))}}}", sid);
                        }
                    }
                    else
                    {
                        lock (blocked)
                        {
                            blocked.Add(url);
                        }

                        _ = cdp!.Post("Fetch.failRequest", $"{{\"requestId\":{J.Str(requestId)},\"errorReason\":\"BlockedByClient\"}}", sid);
                    }

                    break;
                }

            case "Runtime.bindingCalled":
                {
                    var payload = p.GetProperty("payload").GetString()!;
                    if (payload.Contains("\"ready\"", StringComparison.Ordinal))
                    {
                        readyPayload = payload;
                        ready.Set();
                    }
                    else if (payload.Contains("\"pong\"", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref pongs);
                    }

                    break;
                }

            case "Target.targetCreated":
            case "Target.targetDestroyed":
            case "Target.detachedFromTarget":
            case "Inspector.detached":
            case "Target.targetCrashed":
                lock (targetEvents)
                {
                    targetEvents.Add($"{clock.Elapsed.TotalMilliseconds:F0}:{method}:{(p.ValueKind == JsonValueKind.Object ? Short(p.GetRawText()) : string.Empty)}");
                }

                break;
        }
    };
    cdp.Start();

    using (var version = cdp.Call("Browser.getVersion"))
    {
        R("first_reply_ms_after_launch", (clock.Elapsed.TotalMilliseconds - launchAt).ToString("F1", CultureInfo.InvariantCulture));
        var r = version.RootElement.GetProperty("result");
        R("product", r.GetProperty("product").GetString()!);
        R("protocolVersion", r.GetProperty("protocolVersion").GetString()!);
        R("userAgent", r.GetProperty("userAgent").GetString()!);
    }

    _ = cdp.Call("Target.setDiscoverTargets", "{\"discover\":true}");

    string? targetId = null;
    for (var attempt = 0; attempt < 400 && targetId is null; attempt++)
    {
        using var targets = cdp.Call("Target.getTargets");
        foreach (var t in targets.RootElement.GetProperty("result").GetProperty("targetInfos").EnumerateArray())
        {
            if (t.GetProperty("type").GetString() == "page")
            {
                targetId = t.GetProperty("targetId").GetString();
                R("page_target", Short(t.GetRawText()));
                break;
            }
        }

        if (targetId is null)
        {
            Thread.Sleep(10);
        }
    }

    if (targetId is null)
    {
        using var created = cdp.Call("Target.createTarget", "{\"url\":\"about:blank\"}");
        targetId = created.RootElement.GetProperty("result").GetProperty("targetId").GetString();
        R("page_target", "created about:blank");
    }

    R("page_target_ms_after_launch", (clock.Elapsed.TotalMilliseconds - launchAt).ToString("F1", CultureInfo.InvariantCulture));

    using (var attached = cdp.Call("Target.attachToTarget", $"{{\"targetId\":{J.Str(targetId!)},\"flatten\":true}}"))
    {
        session = attached.RootElement.GetProperty("result").GetProperty("sessionId").GetString();
    }

    _ = cdp.Call("Page.enable", "{}", session);
    _ = cdp.Call("Runtime.enable", "{}", session);
    _ = cdp.Call("Runtime.addBinding", "{\"name\":\"browseraiHost\"}", session);
    _ = cdp.Call("Fetch.enable", "{\"patterns\":[{\"urlPattern\":\"*\"}]}", session);

    if (mode == "app")
    {
        R("titles_before_navigate", Titles(started.Process.dwProcessId));
    }

    _ = cdp.Call("Page.navigate", $"{{\"url\":{J.Str(Origin + "/")}}}", session);

    if (!ready.Wait(30000))
    {
        throw new TimeoutException("The page never called the binding.");
    }

    var readyMs = clock.Elapsed.TotalMilliseconds;
    R("page_ready_ms_after_launch", (readyMs - launchAt).ToString("F1", CultureInfo.InvariantCulture));
    R("page_ready_ms_after_probe_main", readyMs.ToString("F1", CultureInfo.InvariantCulture));
    R("page_said", readyPayload!);
    File.WriteAllText(readyFile, readyMs.ToString("F1", CultureInfo.InvariantCulture));

    // Host -> page -> host round trips: Runtime.evaluate calls a page function that calls the binding.
    var trips = new List<double>();
    for (var i = 0; i < 40; i++)
    {
        var before = Volatile.Read(ref pongs);
        var t0 = clock.Elapsed.TotalMilliseconds;
        using var _e = cdp.Call("Runtime.evaluate", $"{{\"expression\":\"window.hostPing({i})\",\"returnByValue\":true}}", session);
        var spin = Stopwatch.StartNew();
        while (Volatile.Read(ref pongs) == before && spin.ElapsedMilliseconds < 2000)
        {
            Thread.SpinWait(50);
        }

        trips.Add(clock.Elapsed.TotalMilliseconds - t0);
    }

    trips.Sort();
    R("round_trip_ms", string.Create(CultureInfo.InvariantCulture, $"p50={trips[trips.Count / 2]:F2} min={trips[0]:F2} max={trips[^1]:F2} n={trips.Count} pongs={pongs}"));
    lock (served)
    {
        R("served_over_the_pipe", string.Join(" ", served));
    }

    var hold = Opt("hold");
    if (hold > 0)
    {
        Thread.Sleep(hold);
    }

    if (mode == "app")
    {
        var browserPid = started.Process.dwProcessId;
        R("foreground_window_on_this_desktop", $"0x{W.GetForegroundWindow():X}");
        var tops = Win.TopLevel();
        R("top_level_windows_on_desktop", tops.Count.ToString(CultureInfo.InvariantCulture));
        var index = 0;
        foreach (var t in tops)
        {
            var mine = t.ProcessId == browserPid ? "browser" : $"pid {t.ProcessId}";
            R("window", $"0x{t.Handle:X} owner={mine} class={t.Class} visible={t.Visible} rect={t.Rect} style=0x{t.Style:X8} ex=0x{t.ExStyle:X8} ownerWindow=0x{t.Owner:X} title=[{t.Title}]");
            if (t.ProcessId == browserPid && t.Visible && (t.Rect.Right - t.Rect.Left) > 50)
            {
                if (Opt("shots") == 1)
                {
                    R("capture", Win.Capture(t.Handle, Path.Combine(outDir, $"{label}-window{index}.png")));
                    R("icon", Win.DumpIcon(t.Handle, Path.Combine(outDir, $"{label}-icon{index}.png")));
                }

                R("taskbar_identity", Win.TaskbarIdentity(t.Handle));
                if (Opt("aumid") == 1)
                {
                    R("taskbar_identity_after_set", Win.TaskbarIdentity(t.Handle, "BrowserAI.b2probe"));
                }

                index++;
            }
        }
    }

    foreach (var m in Win.MessageWindows("Chrome_MessageWindow"))
    {
        var mine = m.ProcessId == started.Process.dwProcessId ? "browser" : $"pid {m.ProcessId}";
        R("message_window", $"0x{m.Handle:X} owner={mine} title=[{m.Title}] equals_user_data_dir={string.Equals(m.Title, userData, StringComparison.OrdinalIgnoreCase)}");
    }

    if (Opt("second") == 1)
    {
        var secondFlags = new List<string> { $"--user-data-dir={userData}", "--no-first-run", "--no-default-browser-check" };
        secondFlags.Add(mode == "headless" ? "--headless=new" : "--app=data:text/html,<title>Second</title>");
        var t0 = clock.Elapsed.TotalMilliseconds;
        var second = Chromium.Launch(chrome, secondFlags);
        var secondCdp = new CdpPipe(second.WriteToBrowser, second.ReadFromBrowser, clock);
        secondCdp.Start();
        var wait = W.WaitForSingleObject(second.Process.hProcess, 20000);
        _ = W.GetExitCodeProcess(second.Process.hProcess, out var secondCode);
        var took = clock.Elapsed.TotalMilliseconds - t0;
        Thread.Sleep(1500);
        R("second_start", string.Create(CultureInfo.InvariantCulture, $"pid={second.Process.dwProcessId} exited={(wait == 0)} exit_code={secondCode} after_ms={took:F0} its_pipe_read_ended={secondCdp.ReadEnded} its_pipe_messages={secondCdp.MessagesIn}"));
        if (wait != 0)
        {
            _ = W.TerminateProcess(second.Process.hProcess, 9);
        }

        secondCdp.Dispose();
        _ = W.CloseHandle(second.Process.hProcess);
        _ = W.CloseHandle(second.Process.hThread);
        if (mode == "app")
        {
            R("titles_after_second_start", Titles(started.Process.dwProcessId));
        }

        using var targets = cdp.Call("Target.getTargets");
        R("targets_after_second_start", Short(targets.RootElement.GetProperty("result").GetProperty("targetInfos").GetRawText(), 900));
    }

    var idle = Opt("idle");
    if (idle > 0)
    {
        Thread.Sleep(idle);
        R("idle_done", $"sink_requests={sink?.Requests ?? -1}");
    }

    if (Opt("navout") == 1)
    {
        try
        {
            using var nav = cdp.Call("Page.navigate", "{\"url\":\"https://example.com/outside\"}", session);
            R("navigate_outside", Short(nav.RootElement.GetProperty("result").GetRawText()));
        }
        catch (Exception failure)
        {
            R("navigate_outside", "threw: " + failure.Message);
        }

        Thread.Sleep(300);
    }

    lock (blocked)
    {
        R("blocked_by_the_host", blocked.Count == 0 ? "(none)" : string.Join(" ", blocked));
    }

    lock (targetEvents)
    {
        R("target_events", string.Join(" | ", targetEvents));
    }

    // Ending it.
    var how = OptS("close", "cdp");
    var closeAt = clock.Elapsed.TotalMilliseconds;
    switch (how)
    {
        case "cdp":
            try
            {
                _ = cdp.Call("Browser.close", "{}", null, 5000);
            }
            catch (Exception failure)
            {
                R("browser_close_call", failure.Message);
            }

            break;
        case "wm":
            foreach (var t in Win.TopLevel())
            {
                if (t.ProcessId == started.Process.dwProcessId && t.Visible && t.Class.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal))
                {
                    R("wm_close_posted", $"0x{t.Handle:X} ok={W.PostMessageW(t.Handle, 0x0010, 0, 0)}");
                }
            }

            break;
        case "pipe":
            cdp.CloseWriteEnd();
            break;
    }

    var ended = W.WaitForSingleObject(started.Process.hProcess, 30000);
    _ = W.GetExitCodeProcess(started.Process.hProcess, out var code);
    var endMs = clock.Elapsed.TotalMilliseconds;
    Thread.Sleep(200);
    R("ended", string.Create(CultureInfo.InvariantCulture, $"how={how} exited={(ended == 0)} exit_code={code} ms_after_request={(endMs - closeAt):F0} pipe_read_ended={cdp.ReadEnded} pipe_read_ended_ms_after_request={(cdp.ReadEndedAtMs - closeAt):F0} read_end_error={cdp.ReadEndError}"));
    if (ended != 0)
    {
        _ = W.TerminateProcess(started.Process.hProcess, 9);
        R("terminated", "the browser did not exit by itself in 30 s");
    }

    Thread.Sleep(1500);
    R("profile", ProfileSize(userData));
    exitCode = 0;
}
catch (Exception failure)
{
    R("FAILED", failure.ToString().Replace("\r", " ").Replace("\n", " "));
    if (started is not null)
    {
        _ = W.TerminateProcess(started.Process.hProcess, 9);
    }
}
finally
{
    cdp?.Dispose();
    sink?.Dispose();
    R("done", exitCode.ToString(CultureInfo.InvariantCulture));
    Flush();
}

return exitCode;

static string Short(string text, int max = 300) => text.Length <= max ? text : text[..max] + "...";

static string Titles(uint browserPid)
{
    var parts = new List<string>();
    foreach (var t in Win.TopLevel())
    {
        if (t.ProcessId == browserPid && t.Visible)
        {
            parts.Add($"{t.Class}:[{t.Title}]");
        }
    }

    return parts.Count == 0 ? "(no visible top-level window)" : string.Join(" ", parts);
}

static string ProfileSize(string directory)
{
    try
    {
        long bytes = 0;
        var files = 0;
        foreach (var f in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                bytes += new FileInfo(f).Length;
                files++;
            }
            catch (IOException)
            {
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"files={files} bytes={bytes} MB={bytes / 1048576.0:F1}");
    }
    catch (Exception failure)
    {
        return failure.Message;
    }
}

// An orange square with a dark centre: easy to tell from Chrome's own icon in a dump.
static byte[] IconPixels()
{
    var rgb = new byte[32 * 32 * 3];
    for (var y = 0; y < 32; y++)
    {
        for (var x = 0; x < 32; x++)
        {
            var inner = x >= 10 && x < 22 && y >= 10 && y < 22;
            var i = ((y * 32) + x) * 3;
            rgb[i] = (byte)(inner ? 20 : 255);
            rgb[i + 1] = (byte)(inner ? 20 : 122);
            rgb[i + 2] = (byte)(inner ? 20 : 0);
        }
    }

    return rgb;
}

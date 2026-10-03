// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// stest: drives the option S listener (sprobe) with raw requests and with a
// real headless Chromium, and records what the gate did. JIT: the instrument.
//
//   stest <sprobe.exe> <chrome.exe> <userDataDir> <outDir> <label>
//
// Meant to be started by the b2 driver, so it and everything it starts live on a
// private desktop inside a kill-on-close job.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

var clock = Stopwatch.StartNew();
var sprobe = Path.GetFullPath(args[0]);
var chrome = Path.GetFullPath(args[1]);
var userData = Path.GetFullPath(args[2]);
var outDir = Path.GetFullPath(args[3]);
var label = args[4];
Directory.CreateDirectory(outDir);
var portFile = Path.Combine(outDir, label + ".port");
var logPath = Path.Combine(outDir, label + ".sprobe.log");
var resultFile = Path.Combine(outDir, label + ".result.txt");
File.Delete(portFile);
var results = new List<string>();

void R(string key, string value)
{
    results.Add($"{clock.Elapsed.TotalMilliseconds,9:F1}\t{key}\t{value}");
    File.WriteAllLines(resultFile, results);
}

var psi = new ProcessStartInfo(sprobe)
{
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};
psi.ArgumentList.Add(portFile);
psi.ArgumentList.Add(logPath);
var server = Process.Start(psi)!;
server.OutputDataReceived += (_, _) => { };
server.ErrorDataReceived += (_, e) =>
{
    if (e.Data is { Length: > 0 })
    {
        Console.Error.WriteLine("sprobe: " + e.Data);
    }
};
server.BeginOutputReadLine();
server.BeginErrorReadLine();

while (!File.Exists(portFile))
{
    Thread.Sleep(5);
}

var parts = File.ReadAllText(portFile).Split('\t');
var port = int.Parse(parts[0], CultureInfo.InvariantCulture);
var token = parts[1];
var otherPort = int.Parse(parts[2], CultureInfo.InvariantCulture);
var origin = $"http://127.0.0.1:{port}";
var url = $"{origin}/{token}/";
R("listening", $"port={port} other={otherPort} token_chars={token.Length} sprobe_start_ms={parts[3]}");

string[] ServerLog()
{
    using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
}

(string Status, string Head, string Body) Raw(IPAddress address, string request)
{
    using var s = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ReceiveTimeout = 5000, SendTimeout = 5000, NoDelay = true };
    s.Connect(new IPEndPoint(address, port));
    s.Send(Encoding.ASCII.GetBytes(request));
    using var all = new MemoryStream();
    var buffer = new byte[8192];
    try
    {
        int n;
        while ((n = s.Receive(buffer)) > 0)
        {
            all.Write(buffer, 0, n);
        }
    }
    catch (SocketException)
    {
    }

    var text = Encoding.UTF8.GetString(all.ToArray());
    var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
    var head = split < 0 ? text : text[..split];
    return (head.Split("\r\n")[0], head, split < 0 ? string.Empty : text[(split + 4)..]);
}

var lo = IPAddress.Loopback;
var host = $"127.0.0.1:{port}";
var wrong = new string('A', token.Length);
var pass = 0;
var fail = 0;

void Row(string name, string expect, Func<string> run)
{
    string got;
    try
    {
        got = run();
    }
    catch (SocketException failure)
    {
        got = "no connection: " + failure.SocketErrorCode;
    }

    var ok = got.Contains(expect, StringComparison.Ordinal);
    if (ok)
    {
        pass++;
    }
    else
    {
        fail++;
    }

    R("raw", $"{(ok ? "ok  " : "FAIL")} {name,-64} expect[{expect}] got[{got}]");
}

string Get(string target, string hostHeader, string extra = "") => $"GET {target} HTTP/1.1\r\nHost: {hostHeader}\r\n{extra}Connection: close\r\n\r\n";
string Post(string target, string extra, string body) => $"POST {target} HTTP/1.1\r\nHost: {host}\r\n{extra}Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";

var first = Raw(lo, Get($"/{token}/", host));
R("page_response_head", first.Head.Replace("\r\n", " | "));
Row("R01 page with the token and our Host", "200", () => first.Status);
Row("R01b no Set-Cookie and no CORS header on it", "clean", () => first.Head.Contains("Set-Cookie", StringComparison.OrdinalIgnoreCase) || first.Head.Contains("Access-Control", StringComparison.OrdinalIgnoreCase) ? "dirty" : "clean");
Row("R02 no token", "404", () => Raw(lo, Get("/", host)).Status);
Row("R03 a wrong token of the right length", "404", () => Raw(lo, Get($"/{wrong}/", host)).Status);
Row("R04 the token, Host: localhost:port", "404", () => Raw(lo, Get($"/{token}/", $"localhost:{port}")).Status);
Row("R05 the token, Host: attacker.example:port (rebinding)", "404", () => Raw(lo, Get($"/{token}/", $"attacker.example:{port}")).Status);
Row("R06 the token, HTTP/1.0 with no Host", "404", () => Raw(lo, $"GET /{token}/ HTTP/1.0\r\n\r\n").Status);
Row("R07 the token, two Host headers", "404", () => Raw(lo, $"GET /{token}/ HTTP/1.1\r\nHost: {host}\r\nHost: attacker.example\r\nConnection: close\r\n\r\n").Status);
Row("R08 the token, GET with a foreign Origin and cross-site", "404", () => Raw(lo, Get($"/{token}/state", host, "Origin: http://attacker.example\r\nSec-Fetch-Site: cross-site\r\n")).Status);
Row("R09 the write, as our page sends it", "204", () => Raw(lo, Post($"/{token}/act", $"Origin: {origin}\r\nSec-Fetch-Site: same-origin\r\nContent-Type: application/json\r\n", "{}")).Status);
Row("R10 the write with no Origin", "404", () => Raw(lo, Post($"/{token}/act", "Content-Type: application/json\r\n", "{}")).Status);
Row("R11 the write from a foreign Origin, text/plain", "404", () => Raw(lo, Post($"/{token}/act", "Origin: http://attacker.example\r\nContent-Type: text/plain\r\n", "hi")).Status);
Row("R12 the write, our Origin, form content type", "404", () => Raw(lo, Post($"/{token}/act", $"Origin: {origin}\r\nContent-Type: application/x-www-form-urlencoded\r\n", "a=1")).Status);
Row("R13 a preflight", "404", () => Raw(lo, $"OPTIONS /{token}/act HTTP/1.1\r\nHost: {host}\r\nOrigin: http://attacker.example\r\nAccess-Control-Request-Method: POST\r\nConnection: close\r\n\r\n").Status);
Row("R14 the token, a route that does not exist", "404", () => Raw(lo, Get($"/{token}/nosuch", host)).Status);
Row("R15 PUT", "404", () => Raw(lo, $"PUT /{token}/act HTTP/1.1\r\nHost: {host}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").Status);
Row("R16 a 9 KB header", "404", () => Raw(lo, Get($"/{token}/", host, "X-Pad: " + new string('a', 9000) + "\r\n")).Status);
Row("R17 a body announced at 100,000 bytes", "404", () => Raw(lo, $"POST /{token}/act HTTP/1.1\r\nHost: {host}\r\nOrigin: {origin}\r\nContent-Type: application/json\r\nContent-Length: 100000\r\nConnection: close\r\n\r\n").Status);
Row("R18 a chunked body", "404", () => Raw(lo, $"POST /{token}/act HTTP/1.1\r\nHost: {host}\r\nOrigin: {origin}\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n2\r\n{{}}\r\n0\r\n\r\n").Status);
Row("R19 not HTTP at all", "404", () => Raw(lo, "\x16\x03\x01\x02\x00\x01\x00\x01\xfc\x03\x03 garbage\r\n\r\n").Status);
Row("R20 the same port on [::1]", "no connection", () => Raw(IPAddress.IPv6Loopback, Get($"/{token}/", $"[::1]:{port}")).Status);
foreach (var address in NetworkInterface.GetAllNetworkInterfaces()
    .Where(n => n.OperationalStatus == OperationalStatus.Up)
    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
    .Select(u => u.Address)
    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
    .Take(2))
{
    // The address itself is not recorded: only whether the port answered on it.
    Row("R21 the same port on a non-loopback IPv4 address of this machine", "no connection", () => Raw(address, Get($"/{token}/", host)).Status);
}

Row("R22 the counter moved once, by R09 alone", "{\"counter\":1}", () => Raw(lo, Get($"/{token}/state", host)).Body);
R("raw_summary", $"pass={pass} fail={fail}");

// ---- a real browser ----
var flags = new List<string>
{
    $"--user-data-dir={userData}", "--headless=new", "--no-first-run", "--no-default-browser-check",
    "--host-resolver-rules=MAP attacker.example 127.0.0.1",
    "--disable-background-networking", "--disable-component-update", "--disable-sync", "--disable-extensions", "--disable-default-apps",
    "--disable-component-extensions-with-background-pages", "--metrics-recording-only", "--no-service-autorun",
    "--disable-features=MediaRouter,DialMediaRouteProvider,OptimizationHints,Translate",
};
var started = Chromium.Launch(chrome, flags);
using var cdp = new CdpPipe(started.WriteToBrowser, started.ReadFromBrowser, clock);
var destroyed = new List<string>();
cdp.OnEvent = (method, p, _) =>
{
    if (method == "Target.targetDestroyed")
    {
        lock (destroyed)
        {
            destroyed.Add(p.GetProperty("targetId").GetString()!);
        }
    }
};
cdp.Start();
using (var v = cdp.Call("Browser.getVersion"))
{
    R("browser", v.RootElement.GetProperty("result").GetProperty("product").GetString()!);
}

_ = cdp.Call("Target.setDiscoverTargets", "{\"discover\":true}");

(string Target, string Session) NewPage(string address)
{
    using var created = cdp.Call("Target.createTarget", $"{{\"url\":{J.Str(address)}}}");
    var target = created.RootElement.GetProperty("result").GetProperty("targetId").GetString()!;
    using var attached = cdp.Call("Target.attachToTarget", $"{{\"targetId\":{J.Str(target)},\"flatten\":true}}");
    var session = attached.RootElement.GetProperty("result").GetProperty("sessionId").GetString()!;
    _ = cdp.Call("Page.enable", "{}", session);
    _ = cdp.Call("Runtime.enable", "{}", session);
    return (target, session);
}

string Eval(string session, string expression)
{
    try
    {
        using var doc = cdp.Call("Runtime.evaluate", $"{{\"expression\":{J.Str(expression)},\"returnByValue\":true,\"awaitPromise\":true}}", session, 20000);
        var result = doc.RootElement.GetProperty("result");
        if (result.TryGetProperty("exceptionDetails", out var ex))
        {
            return "EXCEPTION " + ex.GetRawText();
        }

        var value = result.GetProperty("result");
        return value.TryGetProperty("value", out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()) : value.GetRawText();
    }
    catch (Exception failure)
    {
        return "THREW " + failure.Message;
    }
}

bool WaitLog(Func<string[], bool> condition, int timeoutMs = 10000)
{
    var wait = Stopwatch.StartNew();
    while (wait.ElapsedMilliseconds < timeoutMs)
    {
        if (condition(ServerLog()))
        {
            return true;
        }

        Thread.Sleep(1);
    }

    return false;
}

int Count(string[] lines, string marker) => lines.Count(l => l.Split('\t').Length > 1 && l.Split('\t')[1] == marker);

void WaitFor(string session, string expression, string wanted, int timeoutMs = 10000)
{
    var wait = Stopwatch.StartNew();
    while (wait.ElapsedMilliseconds < timeoutMs)
    {
        if (Eval(session, expression) == wanted)
        {
            return;
        }

        Thread.Sleep(20);
    }
}

// B1: the page, its event stream, and its one write.
var plusBefore = Count(ServerLog(), "+page");
var page1 = NewPage(url);
R("B1_connected", WaitLog(l => Count(l, "+page") > plusBefore).ToString());
WaitFor(page1.Session, "document.getElementById('link') && document.getElementById('link').textContent", "connected");
R("B1_link", Eval(page1.Session, "document.getElementById('link').textContent"));
R("B1_write_status", Eval(page1.Session, "window.act()"));
WaitFor(page1.Session, "document.getElementById('state').textContent.includes('\"counter\":2')", "true");
R("B1_state_pushed", Eval(page1.Session, "document.getElementById('state').textContent"));
R("B1_cookies_and_storage", Eval(page1.Session, "JSON.stringify({cookie: document.cookie, local: localStorage.length, session: sessionStorage.length})"));

// B2: reload, ten times. The gap is what a lifetime rule has to survive.
var gaps = new List<double>();
for (var i = 0; i < 10; i++)
{
    var before = ServerLog();
    var minus = Count(before, "-page");
    var plus = Count(before, "+page");
    _ = cdp.Call("Page.reload", "{}", page1.Session);
    if (!WaitLog(l => Count(l, "-page") > minus && Count(l, "+page") > plus))
    {
        R("B2_reload", $"run {i}: no reconnect seen");
        continue;
    }

    var after = ServerLog();
    double At(string marker, int nth) => double.Parse(after.Where(l => l.Split('\t')[1] == marker).ElementAt(nth).Split('\t')[0], CultureInfo.InvariantCulture);
    gaps.Add(At("+page", plus) - At("-page", minus));
    Thread.Sleep(150);
}

gaps.Sort();
R("B2_reload_gap_ms", gaps.Count == 0 ? "none" : string.Create(CultureInfo.InvariantCulture, $"n={gaps.Count} min={gaps[0]:F1} median={gaps[gaps.Count / 2]:F1} max={gaps[^1]:F1} (negative means the new stream opened before the old one closed)"));
WaitFor(page1.Session, "document.getElementById('link') && document.getElementById('link').textContent", "connected");

// B3: a second tab on the same URL, then the first one goes.
plusBefore = Count(ServerLog(), "+page");
var page2 = NewPage(url);
R("B3_second_tab_connected", WaitLog(l => Count(l, "+page") > plusBefore).ToString());
R("B3_pages_now", ServerLog().Last(l => l.Contains("+page", StringComparison.Ordinal)).Trim());
var minusBefore = Count(ServerLog(), "-page");
var t0 = clock.Elapsed.TotalMilliseconds;
_ = cdp.Call("Target.closeTarget", $"{{\"targetId\":{J.Str(page1.Target)}}}");
var seen = WaitLog(l => Count(l, "-page") > minusBefore);
R("B3_tab_close_seen_by_the_listener_ms", seen ? (clock.Elapsed.TotalMilliseconds - t0).ToString("F1", CultureInfo.InvariantCulture) : "not seen");

// B4: DNS rebinding, with the token already leaked: the browser is told attacker.example is 127.0.0.1.
var denyBefore = ServerLog().Count(l => l.Contains("deny\tHost", StringComparison.Ordinal));
var rebind = NewPage($"http://attacker.example:{port}/{token}/");
Thread.Sleep(800);
R("B4_rebinding_page", Eval(rebind.Session, "JSON.stringify({title: document.title, text: document.body ? document.body.innerText.length : -1, url: location.href.replace(/[A-Za-z0-9_-]{40,}/, '<token>')})"));
R("B4_listener_denied_on_host", (ServerLog().Count(l => l.Contains("deny\tHost", StringComparison.Ordinal)) - denyBefore).ToString(CultureInfo.InvariantCulture));
R("B4_rebinding_fetch", Eval(rebind.Session, $"fetch('http://attacker.example:{port}/{token}/state').then(r => r.status + ' ' + r.type, e => 'ERR ' + e.message)"));

// B5: some other site, which has somehow learned the whole URL.
var other = NewPage($"http://attacker.example:{otherPort}/");
Thread.Sleep(500);
R("B5_other_site_origin", Eval(other.Session, "location.origin"));
string Deny() => string.Join(" ", ServerLog().Where(l => l.Contains("deny\t", StringComparison.Ordinal)).TakeLast(1).Select(l => string.Join(" ", l.Trim().Split('\t').Skip(1)).Replace(token, "<token>")));
R("B5a_cross_site_post_text_plain", Eval(other.Session, $"fetch('{url}act', {{method:'POST', mode:'no-cors', headers:{{'Content-Type':'text/plain'}}, body:'x'}}).then(r => r.type + ' ' + r.status, e => 'ERR ' + e.message)") + " || listener: " + Deny());
R("B5b_cross_site_get_no_cors", Eval(other.Session, $"fetch('{url}state', {{mode:'no-cors'}}).then(r => r.type + ' ' + r.status, e => 'ERR ' + e.message)") + " || listener: " + Deny());
R("B5c_cross_site_get_cors", Eval(other.Session, $"fetch('{url}state').then(r => r.type + ' ' + r.status, e => 'ERR ' + e.message)") + " || listener: " + Deny());
R("B5d_cross_site_json_post", Eval(other.Session, $"fetch('{url}act', {{method:'POST', headers:{{'Content-Type':'application/json'}}, body:'{{}}'}}).then(r => r.type + ' ' + r.status, e => 'ERR ' + e.message)") + " || listener: " + Deny());
R("B5e_iframe", Eval(other.Session, $"new Promise(res => {{ const f = document.createElement('iframe'); f.onload = () => {{ let d = 'unreadable'; try {{ d = f.contentDocument ? f.contentDocument.title + '/' + f.contentDocument.body.innerText.length : 'null document'; }} catch (e) {{ d = 'threw ' + e.name; }} res(d); }}; f.src = '{url}'; document.body.appendChild(f); setTimeout(() => res('no load event'), 4000); }})") + " || listener: " + Deny());
R("B5f_form_post", Eval(other.Session, $"new Promise(res => {{ const f = document.createElement('iframe'); f.name = 'sink'; document.body.appendChild(f); const form = document.createElement('form'); form.method = 'post'; form.action = '{url}act'; form.target = 'sink'; document.body.appendChild(form); form.submit(); setTimeout(() => res('submitted'), 800); }})") + " || listener: " + Deny());
R("B5g_websocket", Eval(other.Session, $"new Promise(res => {{ const w = new WebSocket('ws://127.0.0.1:{port}/{token}/events'); w.onopen = () => res('OPEN'); w.onerror = () => res('error'); setTimeout(() => res('timeout'), 4000); }})") + " || listener: " + Deny());
R("B5_counter_after_all_of_that", Raw(lo, Get($"/{token}/state", host)).Body);

// B6: can the page close its own tab?
var page3 = NewPage(url);
Thread.Sleep(600);
lock (destroyed)
{
    destroyed.Clear();
}

R("B6_history_length", Eval(page3.Session, "String(history.length)"));
_ = Eval(page3.Session, "window.close(); 'called'");
Thread.Sleep(800);
lock (destroyed)
{
    R("B6_window_close_closed_the_tab", destroyed.Contains(page3.Target).ToString());
}

// What the browser itself sent on the legitimate requests.
foreach (var line in ServerLog().Where(l => l.Contains("admit\t", StringComparison.Ordinal)).Select(l => string.Join(" ", l.Trim().Split('\t').Skip(1))).Distinct().Take(12))
{
    R("admitted", line);
}

try
{
    _ = cdp.Call("Browser.close", "{}", null, 5000);
}
catch (Exception)
{
}

_ = W.WaitForSingleObject(started.Process.hProcess, 15000);
_ = Raw(lo, Post($"/{token}/quit", $"Origin: {origin}\r\nContent-Type: application/json\r\n", "{}"));
if (!server.WaitForExit(5000))
{
    server.Kill();
}

R("done", "0");
return 0;

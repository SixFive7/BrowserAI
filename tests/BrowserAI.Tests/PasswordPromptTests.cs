// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The browser never asks a caller to save a password, because a caller cannot
/// answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is about.</b> A sign-in POST makes Chromium offer to remember the
/// credential, and the offer is a top-level window over the page. An agent
/// driving the session cannot dismiss it -- it is not in the accessibility
/// snapshot the agent reads -- so the page underneath stays half-covered and the
/// next tool call answers about a page nobody can see. Q255.
/// </para>
/// <para>
/// ⚠️ <b>THE CONTROL IS THE TEST, and that is the whole design.</b> Asserting
/// that no window appeared is asserting an absence, and an absence is what a
/// browser that never started looks like too. So both arms run in one pass: a
/// control child launched with the switches stripped out, which MUST produce a
/// window, and the product child, which must produce none. The control is also
/// the CLOCK -- the product arm is sampled only once the control has shown its
/// window, so nothing here bounds the wait with a number somebody invented.
/// </para>
/// <para>
/// <b>Measured 2026-09-23 @ chromium 1246 (154.0.8037.0), headless, Windows
/// 10.0.26200</b>, which is what this arm re-establishes: with the switches
/// absent the POST produced <b>two</b> new top-level windows; with them present,
/// <b>zero</b>. Headless on purpose -- the prompt appears in headless Chromium,
/// which is the finding that makes this testable at all without putting a window
/// on the maintainer's screen. The whole measurement, including the two facts
/// that are absences, is in
/// <see href="../../kb/playwright/configuration.md">kb: configuration</see>.
/// </para>
/// </remarks>
internal sealed class PasswordPromptTests
{
    private static readonly TimeSpan LaunchPatience = TestDefaults.BrowserHang;

    /// <summary>
    /// A sign-in POST opens a window in a child launched without the switches and
    /// none in one launched with them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Windows are keyed by HANDLE and filtered by the job's pid set.</b> A
    /// live machine opens and closes top-level windows constantly, so a
    /// before-and-after count of the desktop is a flake dressed as an assertion;
    /// what makes it specific is that the window belongs to a process inside that
    /// child's own job. Handles and not counts, because a window closing and
    /// another opening between the two samples leaves a count unmoved.
    /// </para>
    /// <para>
    /// <b>The POST is asserted as the PRECONDITION.</b> The prompt follows a form
    /// submission, so a test whose form never posted would report no window and
    /// pass. The local server records every POST it receives and both arms are
    /// required to have posted before anything is concluded.
    /// </para>
    /// <para>
    /// ⚠️ <b>The failure message carries the class name and the title and asserts
    /// NEITHER.</b> Which window Chromium opens for this is upstream's choice and
    /// may change; what this test holds is that a window appeared at all. Naming
    /// the class in the message is how the next reader finds out what it was
    /// without this test claiming to know.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoWindowOpensOnASignInPostAndAControlChildProvesTheProbeCanSeeOne()
    {
        SuiteEnvironment.RequireProvisionedChromium();

        using var scratch = ScratchDirectory.Create("password-prompt");
        using var site = LoginSite.Start();

        // ⚠️ DECLARED BEFORE THE CHILDREN, so it is released after them: a
        // browser whose session guard is unheld is a stray to every sweeper on
        // this machine. See SessionGuards.
        using var guards = new SessionGuards();

        await using var product = await StartAsync(scratch, "product", withArguments: true, guards);
        await using var control = await StartAsync(scratch, "control", withArguments: false, guards);

        // ⚠️ THE CONTROL'S CONFIGURATION IS ASSERTED, not assumed. A control
        // that quietly kept the switches would show no window either, and the
        // pass would then be the two arms agreeing about nothing.
        await Assert.That(await ArgumentsOfAsync(product)).IsEquivalentTo(BrowserConfiguration.ChromiumArguments);
        await Assert.That(await ArgumentsOfAsync(control)).DoesNotContain("--enable-automation");

        // ⚠️ THE BROWSER IS UP BEFORE ANYTHING IS COUNTED. A child answers
        // `initialize` without starting a browser, so a set sampled then is
        // empty and every window the browser legitimately owns reads as new --
        // which is how the first run of this arm reported a
        // `Chrome_WidgetWin_0` against the product child and called it a prompt.
        await Assert.That(await NavigateAsync(product, site)).IsTrue();
        await Assert.That(await NavigateAsync(control, site)).IsTrue();

        var productBefore = WindowsOf(product);
        var controlBefore = WindowsOf(control);

        await Assert.That(await SubmitAsync(product)).IsTrue();
        await Assert.That(await SubmitAsync(control)).IsTrue();

        // The precondition. Two POSTs, one per arm, or neither arm asked the
        // question this test is about.
        await Assert.That(site.Posts).IsEqualTo(2);

        // ⚠️ THE CONTROL IS THE CLOCK. The product arm is read only once the
        // control has opened its window, so the product has had at least as long
        // as the prompt actually took on this machine. LaunchPatience is a hang
        // detector and nothing else: reaching it means the control never showed
        // a prompt, which is a broken probe and is reported as one.
        var appeared = await NewWindowAsync(control, controlBefore, LaunchPatience);

        await Assert.That(appeared.Count)
            .IsGreaterThan(0)
            .Because("the control child launched WITHOUT --enable-automation and must show the password prompt; no new window means this probe cannot see one and the product arm below proves nothing");

        var leaked = WindowsOf(product).Except(productBefore).ToList();

        await Assert.That(string.Join(
            Environment.NewLine,
            leaked.Select(window =>
                $"the product child opened a window: handle 0x{window.ToString("X", CultureInfo.InvariantCulture)}, class '{TopLevelWindows.ClassNameOf(window)}', title '{MessageWindows.TitleOf(window)}'")))
            .IsEmpty()
            .Because($"a sign-in POST must open nothing in front of the caller; the control opened {appeared.Count.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// The Firefox preference that suppresses the same prompt reaches the running
    /// child.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the weaker claim and it is deliberately the weaker claim.</b>
    /// It asks the child's own resolved configuration, the way
    /// <c>FirefoxTests</c> does, because that is the half that catches a key
    /// upstream renamed -- <c>loadConfig</c> is a bare <c>JSON.parse</c> with no
    /// schema validation, so a dropped key is dropped in silence.
    /// </para>
    /// <para>
    /// ⚠️ <b>WHAT IT DOES NOT ASSERT IS BEHAVIOUR, because the behaviour was not
    /// established.</b> Measured 2026-09-23 @ firefox 1549 (156.0): with the
    /// preference set no prompt appeared -- and none appeared in the control
    /// either, on a fresh profile with the preference absent. So there is no
    /// arm here that could go red for the right reason, and writing one that
    /// asserted the absence would be asserting a default this product does not
    /// control. The kb entry says the same.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]

    // ⚠️ THE SAME KEY EVERY REAL-FIREFOX ARM CARRIES, and it is a correctness
    // requirement and not a load one: `FirefoxTests`' preflight arm asserts that
    // NO Firefox appeared while it ran, so a Firefox this arm launches would make
    // that one fail for a reason belonging to this file. Leaving the key off cost
    // a three-minute navigate timeout in the attribution arm on the first full
    // pass after this file was added, which is the same contention wearing a
    // different failure.
    [NotInParallel("stray-sweep")]
    public async Task TheFirefoxRememberSignonsPreferenceReachesTheChild()
    {
        SuiteEnvironment.RequireProvisionedFirefox();

        using var scratch = ScratchDirectory.Create("password-prompt-firefox");

        // Held for the child's life, for the reason SessionGuards gives.
        using var guards = new SessionGuards();

        await using var child = await StartAsync(scratch, "firefox", withArguments: true, guards, ProvisionedBrowsers.Firefox);

        var resolved = await ResolvedConfigAsync(child);
        var prefs = resolved["browser"]?["launchOptions"]?["firefoxUserPrefs"];

        await Assert.That(prefs).IsNotNull();
        await Assert.That((bool?)prefs![FirefoxProfile.RememberSignonsPreference]).IsFalse();

        // And the one beside it, so a generator that wrote the new key by
        // replacing the old one is red and not green.
        await Assert.That((bool?)prefs[FirefoxProfile.RestartRegistrationPreference]).IsFalse();
    }

    /// <summary>Starts one child against its own session, through the product's launch funnel.</summary>
    /// <param name="scratch">Where the session and the config file live.</param>
    /// <param name="label">The session's directory name.</param>
    /// <param name="withArguments">Whether the generated config keeps its Chromium switches.</param>
    /// <param name="browser">The family.</param>
    /// <param name="guards">
    /// Where this session's guard is held for the life of the child. It is not
    /// optional and it is not tidiness: see <see cref="SessionGuards"/>.
    /// </param>
    /// <returns>A started raw client.</returns>
    private static async Task<RawStdioClient> StartAsync(
        ScratchDirectory scratch,
        string label,
        bool withArguments,
        SessionGuards guards,
        string browser = ProvisionedBrowsers.Chromium)
    {
        var session = NewSession(scratch, label, browser, guards);
        var config = BrowserConfiguration.ForSession(session, headed: false, browser, tracing: false, RunOptions.Default);
        var configFile = Path.Combine(scratch.Path, $"playwright-mcp-{label}.json");

        if (!withArguments)
        {
            // ⚠️ THE CONTROL IS BUILT BY SUBTRACTION FROM THE REAL CONFIG, not
            // by hand. A hand-written config would diverge from the product's in
            // ways nobody tracked, and then the two arms would differ in more
            // than the one thing under test.
            var stripped = JsonNode.Parse(config.Json)?.AsObject()
                ?? throw new InvalidOperationException("the generated configuration is not a JSON object");

            _ = (stripped["browser"]?["launchOptions"]?.AsObject()?.Remove("args"));
            config = config with { Json = System.Text.Encoding.UTF8.GetBytes(stripped.ToJsonString()) };
        }

        var options = ChildLaunch.Create(
            RepositoryPayload.Layout,
            BrowserAiPaths.BrowsersDirectory,
            scratch.Path,
            configFile,
            config);

        var child = RawStdioClient.Start(
            options.Command,
            options.Arguments,
            options.WorkingDirectory,
            options.Environment,
            LaunchPatience);

        _ = await child.InitializeAsync("2025-11-25");
        return child;
    }

    /// <summary>Navigates to the login form, which is also what starts the browser.</summary>
    /// <param name="child">The child to drive.</param>
    /// <param name="site">The local server.</param>
    /// <returns>Whether the call came back without an error.</returns>
    private static async Task<bool> NavigateAsync(RawStdioClient child, LoginSite site)
    {
        var navigated = await child.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = site.Url },
        });

        return (bool?)navigated["isError"] is not true;
    }

    /// <summary>Fills the form and submits it.</summary>
    /// <param name="child">The child to drive.</param>
    /// <returns>Whether the call came back without an error.</returns>
    private static async Task<bool> SubmitAsync(RawStdioClient child)
    {
        var filled = await child.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_evaluate",
            ["arguments"] = new JsonObject
            {
                ["function"] = "() => { document.querySelector('#username').value = 'probe-user';"
                    + " document.querySelector('#password').value = 'Sup3rSecret!probe';"
                    + " document.querySelector('form').submit(); return 'submitted'; }",
            },
        });

        return (bool?)filled["isError"] is not true;
    }

    /// <summary>The Chromium switches the child's own resolved configuration carries.</summary>
    /// <param name="child">The child to ask.</param>
    /// <returns>The switches, or an empty list when the key is absent.</returns>
    private static async Task<IReadOnlyList<string>> ArgumentsOfAsync(RawStdioClient child)
    {
        var resolved = await ResolvedConfigAsync(child);

        return [.. (resolved["browser"]?["launchOptions"]?["args"]?.AsArray() ?? [])
            .Select(argument => (string?)argument ?? string.Empty)];
    }

    /// <summary>The child's own merged configuration, as <c>browser_get_config</c> reports it.</summary>
    /// <remarks>
    /// Upstream's response builder wraps every text section in a heading, so the
    /// JSON is cut out of the answer and not parsed from it whole. Same shape as
    /// <c>FirefoxTests</c>'s reader, and deliberately a second copy: a shared
    /// helper would put a third file between an assertion and the wire.
    /// </remarks>
    /// <param name="child">The child to ask.</param>
    /// <returns>The configuration object.</returns>
    private static async Task<JsonObject> ResolvedConfigAsync(RawStdioClient child)
    {
        var answer = await child.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_get_config",
            ["arguments"] = new JsonObject(),
        });

        var text = string.Concat((answer["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');

        return (start >= 0 && end > start
            ? JsonNode.Parse(text[start..(end + 1)])?.AsObject()
            : null)
            ?? throw new InvalidOperationException($"browser_get_config returned no JSON object: {text}");
    }

    /// <summary>Every top-level window owned by a process inside this child's job.</summary>
    /// <param name="child">The child whose job bounds the question.</param>
    /// <returns>The window handles.</returns>
    private static HashSet<nint> WindowsOf(RawStdioClient child)
    {
        var inTheJob = child.JobProcessIds().ToHashSet();

        return [.. TopLevelWindows.All().Where(window => inTheJob.Contains(TopLevelWindows.ProcessIdOf(window)))];
    }

    /// <summary>Waits until a child owns a window it did not own before.</summary>
    /// <remarks>
    /// The bound is a hang detector: reaching it means no window ever appeared,
    /// which the caller reports as a broken probe. Nothing here is a promptness
    /// claim, and the interval is a polling cost and not a deadline.
    /// </remarks>
    /// <param name="child">The child to watch.</param>
    /// <param name="before">The handles it owned already.</param>
    /// <param name="patience">How long to keep looking before giving up.</param>
    /// <returns>The handles that are new, empty if none ever appeared.</returns>
    private static async Task<List<nint>> NewWindowAsync(RawStdioClient child, HashSet<nint> before, TimeSpan patience)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        while (clock.Elapsed < patience)
        {
            var now = WindowsOf(child).Except(before).ToList();

            if (now.Count > 0)
            {
                return now;
            }

            await Task.Delay(100);
        }

        return [];
    }

    /// <summary>A session directory whose guard is taken and HELD.</summary>
    /// <remarks>
    /// ⚠️ <b>Previously "with its lock taken and released"</b>, and released was
    /// the defect: see <see cref="SessionGuards"/> for the sweep that then ended
    /// the browser launched into it, twice in one gate, by pid.
    /// </remarks>
    /// <param name="scratch">Where it lives.</param>
    /// <param name="label">Its directory name.</param>
    /// <param name="browser">The family the lock records.</param>
    /// <param name="guards">Where the guard is held until the arm ends.</param>
    /// <returns>The session path.</returns>
    private static SessionPath NewSession(ScratchDirectory scratch, string label, string browser, SessionGuards guards)
    {
        var path = SessionPath.For(Path.Combine(scratch.Path, label));
        SessionLayout.Create(path);

        return guards.Take(path, browser, $"password prompt {label}");
    }

    /// <summary>
    /// The session guards the children of one arm hold, released when the arm
    /// ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THEY HAVE TO BE HELD, and until 2026-09-24 they were taken and
    /// dropped on the spot -- which made every browser this file launches a STRAY
    /// by the product's own definition.</b> The stray sweep is machine-wide, and a
    /// browser under our browsers root becomes a stray exactly when its attributed
    /// session directory holds a <c>browserai.lock</c> the sweeper can take
    /// itself. A directory whose guard nobody holds is that.
    /// </para>
    /// <para>
    /// <b>Measured twice in one gate, from the sweep's own record</b>:
    /// <i>"Terminated a stray browser: pid=119332 image=...chrome.exe
    /// session=...password-prompt..."</i> -- the same pid the failing arm's own
    /// Playwright log carried, which then reported <i>"Target page, context or
    /// browser has been closed"</i>. The product did what it promises; this file
    /// was handing it a stray.
    /// </para>
    /// <para>
    /// <b>Holding the guard makes the sweep's second condition refuse by
    /// construction, which no <c>[NotInParallel]</c> key can do</b>: the sweeps
    /// come from every BrowserAI this suite starts, which is roughly a hundred
    /// processes, and not from a sibling arm sharing a key.
    /// </para>
    /// </remarks>
    private sealed class SessionGuards : IDisposable
    {
        private readonly List<SessionLock> _held = [];

        /// <summary>Takes one session's guard and holds it.</summary>
        /// <param name="path">The session directory.</param>
        /// <param name="browser">The family the record names.</param>
        /// <param name="purpose">What the record says it is for.</param>
        /// <returns><paramref name="path"/>, so a caller reads as one expression.</returns>
        public SessionPath Take(SessionPath path, string browser, string purpose)
        {
            var taken = SessionLock.TryAcquire(
                path,
                new SessionLockRequest { Browser = browser, Purpose = purpose },
                NullLogger.Instance);

            _held.Add(taken.Acquired
                ?? throw new InvalidOperationException(
                    $"the suite could not take the guard on '{path.FullPath}', so a browser launched into it would be a stray: {taken.Message}"));

            return path;
        }

        /// <summary>Releases every guard, after the children are gone.</summary>
        public void Dispose()
        {
            foreach (var held in _held)
            {
                held.Dispose();
            }

            _held.Clear();
        }
    }

    /// <summary>
    /// A local site with one login form that posts to itself, and a record of
    /// every POST it received.
    /// </summary>
    /// <remarks>
    /// <b>It posts to itself and answers 200 with a different page</b>, because
    /// that is the shape Chromium treats as a successful sign-in -- a form POST
    /// whose response is not the form again. An <c>HttpListener</c> on the
    /// loopback needs no reservation for a port above the ephemeral floor.
    /// </remarks>
    private sealed class LoginSite : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private int _posts;

        private LoginSite(HttpListener listener, string url)
        {
            _listener = listener;
            Url = url;
        }

        /// <summary>Where the form is.</summary>
        public string Url { get; }

        /// <summary>How many POSTs have arrived.</summary>
        public int Posts => Volatile.Read(ref _posts);

        /// <summary>Binds a loopback port and starts answering.</summary>
        /// <returns>The running site.</returns>
        public static LoginSite Start()
        {
            for (var port = 54_100; port < 54_200; port++)
            {
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");

                try
                {
                    listener.Start();
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                    continue;
                }

                var site = new LoginSite(listener, $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");
                _ = Task.Run(site.ServeAsync, CancellationToken.None);

                return site;
            }

            throw new InvalidOperationException("no loopback port between 54100 and 54199 could be bound");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Close();
            _stopping.Dispose();
        }

        private async Task ServeAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                var post = string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal);

                if (post)
                {
                    _ = Interlocked.Increment(ref _posts);
                }

                var body = Encoding.UTF8.GetBytes(post
                    ? "<!doctype html><title>signed in</title><h1>signed in</h1>"
                    : "<!doctype html><title>BrowserAI password probe</title>"
                        + "<form method=\"post\" action=\"/login\">"
                        + "<input id=\"username\" name=\"username\" autocomplete=\"username\">"
                        + "<input id=\"password\" name=\"password\" type=\"password\" autocomplete=\"current-password\">"
                        + "<button id=\"go\" type=\"submit\">sign in</button></form>");

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = body.Length;

                try
                {
                    await context.Response.OutputStream.WriteAsync(body);
                    context.Response.Close();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }
            }
        }
    }
}

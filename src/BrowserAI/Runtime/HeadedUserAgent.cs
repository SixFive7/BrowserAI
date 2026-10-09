// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Runtime;

/// <summary>
/// The user agent a headed Chromium sends, read from the provisioned browser
/// itself, so a hidden one can send it too.
/// </summary>
/// <remarks>
/// <para>
/// <b>6 b, decided 2026-10-04 by the maintainer, in his words verbatim: <i>"6
/// b"</i></b>, on the lifetime review's question whether a switch between a
/// visible and a hidden browser should change what a server sees, whose option b
/// was <i>"Give headless the headed user agent, derived from the browser"</i>.
/// Measured 2026-10-04 at Chrome for Testing 155.0.8059.12 (<c>chromium-1247</c>):
/// a hidden Chromium (<c>chrome.exe --headless</c>) sends
/// <c>HeadlessChrome/155.0.0.0</c> where a headed one sends <c>Chrome/155.0.0.0</c>,
/// and <c>Sec-CH-UA</c> is the same in both.
/// </para>
/// <para>
/// <b>Derived from the browser and never written down</b>, the maintainer's own
/// condition. The provisioned <c>chrome.exe</c> is asked once for the user agent it
/// sends hidden, by rendering a page that writes <c>navigator.userAgent</c> with
/// <c>--headless --dump-dom</c>, and its product token <c>HeadlessChrome/</c> is
/// replaced by <c>Chrome/</c>. Every version number in the result is the
/// browser's own. Measured 2026-10-04: the ask took 708 to 832 ms over 3 runs and
/// answered exactly the headless string the browser sends to a server.
/// </para>
/// <para>
/// <b>Asked once per browser build and kept beside it</b>, in
/// <see cref="CacheFileName"/> inside the revision's own directory, so a reinstall
/// or a new revision deletes it with the tree it describes. The file carries the
/// executable's length and last write time, and a value that does not match them,
/// or does not read as a Chromium user agent, is asked again.
/// </para>
/// <para>
/// <b>Delivered as Chromium's <c>--user-agent</c> switch on a hidden launch.</b>
/// Measured 2026-10-04 against Playwright's own <c>contextOptions.userAgent</c>:
/// that one is applied page by page after a page exists, so the first request of
/// every tab a session restore reopens still carried <c>HeadlessChrome</c>, and so
/// did a service worker's script fetch. The switch is the browser's own and
/// covers every request from the first. <b>What it costs</b>: with it, Chromium
/// answers the high-entropy client hints -- the full version list, the platform
/// version, the architecture and the bitness -- with empty values, to a page that
/// asks and in the headers a server asks for with <c>Accept-CH</c>; the default
/// hints (<c>Sec-CH-UA</c>, <c>Sec-CH-UA-Mobile</c>, <c>Sec-CH-UA-Platform</c>) are
/// unchanged. See <see href="../../../kb/chromium/fingerprinting.md">kb</see>.
/// </para>
/// <para>
/// <b>When it cannot be asked, the hidden browser keeps its own user agent</b>:
/// a browser that is not installed yet, an ask that fails or answers something
/// that is not a Chromium user agent. That is what every hidden launch sent before
/// this, and the session's log says why.
/// </para>
/// </remarks>
internal static class HeadedUserAgent
{
    /// <summary>The file, inside a Chromium revision's directory, that keeps the answer.</summary>
    public const string CacheFileName = "browserai-user-agent.txt";

    /// <summary>The product token a hidden Chromium sends.</summary>
    public const string HeadlessProduct = "HeadlessChrome/";

    /// <summary>The product token a headed Chromium sends in its place.</summary>
    public const string HeadedProduct = "Chrome/";

    /// <summary>The Chromium switch that sets the user agent for every request the browser makes.</summary>
    public const string Switch = "--user-agent=";

    /// <summary>
    /// How long the ask may take before BrowserAI stops waiting and launches the
    /// browser with its own user agent: a hang detector, never a promptness claim.
    /// </summary>
    /// <remarks>
    /// The ask measured under a second on this machine; thirty seconds is room for
    /// a first start of a freshly extracted browser on a slow disk with a virus
    /// scanner reading every file, and it is spent once per browser build, because
    /// the answer is kept.
    /// </remarks>
    public static TimeSpan AskBound { get; } = SessionTimes.HeadedUserAgentAskBound;

    /// <summary>The page the browser renders: it writes its own user agent as its body.</summary>
    private const string Page = "data:text/html,<title>ua</title><script>document.write(navigator.userAgent)</script>";

    /// <summary>
    /// Builds an ask failed for in this process, by executable and stamp, so a build
    /// is not asked again on every launch and a reinstalled one is.
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> Failed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One ask at a time per executable in this process.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Asking = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The user agent the Chromium at <paramref name="executable"/> sends headed,
    /// from the file beside it or asked of it.
    /// </summary>
    /// <param name="executable">The provisioned <c>chrome.exe</c>, absolute.</param>
    /// <param name="scratchDirectory">Where the ask's throwaway profile goes: this run's own directory.</param>
    /// <param name="logger">Where a failure is said.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The user agent, or <see langword="null"/> when it cannot be had.</returns>
    public static async Task<string?> ForAsync(string executable, string scratchDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        if (!File.Exists(executable))
        {
            return null;
        }

        var cache = CacheFileFor(executable);
        var stamp = StampOf(executable);
        var build = $"{executable}|{stamp}";

        if (Failed.ContainsKey(build))
        {
            return null;
        }

        if (ReadCache(cache, stamp) is { } kept)
        {
            return kept;
        }

        var gate = Asking.GetOrAdd(executable, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Another call in this process may have asked while this one waited.
            if (ReadCache(cache, stamp) is { } meanwhile)
            {
                return meanwhile;
            }

            var answer = await AskAsync(executable, scratchDirectory, logger).ConfigureAwait(false);

            if (Headed(answer) is not { } headed)
            {
                _ = Failed.TryAdd(build, true);
                UserAgentLog.NotDerived(logger, executable, answer ?? "nothing");
                return null;
            }

            WriteCache(cache, stamp, headed, logger);
            UserAgentLog.Derived(logger, executable, headed);

            return headed;
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <summary>
    /// The headed user agent for a hidden one: its one <see cref="HeadlessProduct"/>
    /// token replaced by <see cref="HeadedProduct"/>, and nothing else touched.
    /// </summary>
    /// <param name="headless">What the hidden browser said it sends.</param>
    /// <returns>The headed string, or <see langword="null"/> when the input is not a hidden Chromium's user agent.</returns>
    public static string? Headed(string? headless)
    {
        if (headless is null || !IsPlausible(headless))
        {
            return null;
        }

        var at = headless.IndexOf(HeadlessProduct, StringComparison.Ordinal);

        if (at < 0 || headless.IndexOf(HeadlessProduct, at + 1, StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        var headed = string.Concat(headless.AsSpan(0, at), HeadedProduct, headless.AsSpan(at + HeadlessProduct.Length));

        return IsPlausible(headed) ? headed : null;
    }

    /// <summary>The body text of a page <c>--dump-dom</c> wrote to standard output.</summary>
    /// <param name="dumped">What the browser printed.</param>
    /// <returns>The text between <c>&lt;body&gt;</c> and <c>&lt;/body&gt;</c>, trimmed, or <see langword="null"/>.</returns>
    public static string? BodyOf(string dumped)
    {
        ArgumentNullException.ThrowIfNull(dumped);

        var open = dumped.IndexOf("<body>", StringComparison.Ordinal);
        var close = open < 0 ? -1 : dumped.IndexOf("</body>", open, StringComparison.Ordinal);

        return close < 0 ? null : dumped[(open + "<body>".Length)..close].Trim();
    }

    /// <summary>Whether a string reads as a Chromium user agent and is safe on a command line.</summary>
    /// <param name="value">The string.</param>
    /// <returns>Printable ASCII with no quote, of a sane length, naming Mozilla and Chrome.</returns>
    internal static bool IsPlausible(string value) =>
        value.Length is >= 32 and <= 512
        && value.All(character => character is >= ' ' and <= '~' and not '"')
        && value.StartsWith("Mozilla/5.0 ", StringComparison.Ordinal)
        && value.Contains(HeadedProduct, StringComparison.Ordinal);

    /// <summary>Where the answer for one executable is kept: its revision's own directory.</summary>
    /// <param name="executable">The <c>chrome.exe</c>, two levels below the revision directory.</param>
    /// <returns>The file's path.</returns>
    internal static string CacheFileFor(string executable) =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(executable)!)!, CacheFileName);

    /// <summary>What identifies the build the answer was asked of: the executable's length and last write.</summary>
    /// <param name="executable">The <c>chrome.exe</c>.</param>
    /// <returns>The stamp.</returns>
    internal static string StampOf(string executable)
    {
        var file = new FileInfo(executable);

        return $"{file.Length.ToString(CultureInfo.InvariantCulture)} {file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string? ReadCache(string cache, string stamp)
    {
        try
        {
            var lines = File.ReadAllLines(cache);

            return lines is [var kept, var value] && string.Equals(kept, stamp, StringComparison.Ordinal) && IsPlausible(value)
                && !value.Contains(HeadlessProduct, StringComparison.Ordinal)
                ? value
                : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteCache(string cache, string stamp, string headed, ILogger logger)
    {
        var partial = $"{cache}.{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}.partial";

        try
        {
            File.WriteAllText(partial, $"{stamp}\n{headed}\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(partial, cache, overwrite: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Kept or not, the answer is good for this launch; the next process asks again.
            UserAgentLog.NotKept(logger, cache, failure.Message);

            try
            {
                File.Delete(partial);
            }
            catch (Exception litter) when (litter is IOException or UnauthorizedAccessException)
            {
                // Litter in a directory the next ask writes over.
            }
        }
    }

    /// <summary>Asks the browser, hidden, for the user agent it sends.</summary>
    /// <param name="executable">The <c>chrome.exe</c>.</param>
    /// <param name="scratchDirectory">Where the throwaway profile goes.</param>
    /// <param name="logger">Where a failure is said.</param>
    /// <returns>What it said, or <see langword="null"/>.</returns>
    private static async Task<string?> AskAsync(string executable, string scratchDirectory, ILogger logger)
    {
        var profile = Path.Combine(scratchDirectory, $"user-agent-ask-{Guid.NewGuid():N}");

        try
        {
            _ = Directory.CreateDirectory(profile);

            using var job = JobObject.CreateKillOnClose();
            using var process = JobLauncher.Start(
                job,
                executable,
                [
                    "--headless",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-background-networking",
                    "--disable-component-update",
                    "--disable-sync",
                    "--disable-extensions",
                    $"--user-data-dir={profile}",
                    "--dump-dom",
                    Page,
                ],
                profile,
                ChildEnvironment.Build(
                [
                    new KeyValuePair<string, string>("TEMP", profile),
                    new KeyValuePair<string, string>("TMP", profile),
                ]));

            var output = ReadAllAsync(process.StandardOutput);
            var errors = ReadAllAsync(process.StandardError);

            if (!await process.WaitForExitAsync(AskBound).ConfigureAwait(false))
            {
                UserAgentLog.AskHung(logger, executable, AskBound);
                return null;
            }

            // Drained before the job goes, so nothing the browser wrote is cut.
            var printed = await output.WaitAsync(AskBound).ConfigureAwait(false);
            _ = await errors.WaitAsync(AskBound).ConfigureAwait(false);

            return BodyOf(printed);
        }
        catch (Exception failure) when (failure is Win32Exception or IOException or UnauthorizedAccessException or TimeoutException)
        {
            UserAgentLog.AskFailed(logger, executable, failure.Message);
            return null;
        }
        finally
        {
            List<string> failures = [];
            TreeDelete.Remove(profile, failures);
        }
    }

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);

            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException)
        {
            // The pipe was closed under the read, which ending the job does to an
            // ask that ran out of time.
            return string.Empty;
        }
    }
}

/// <summary>What the derivation of the headed user agent says.</summary>
internal static partial class UserAgentLog
{
    /// <summary>The headed user agent was asked of the browser and kept.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="executable">The browser.</param>
    /// <param name="userAgent">What a hidden launch will send.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Asked {Executable} for the user agent it sends hidden, and a hidden launch now sends the headed one: {UserAgent}")]
    public static partial void Derived(ILogger logger, string executable, string userAgent);

    /// <summary>The browser answered something that is not a hidden Chromium's user agent.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="executable">The browser.</param>
    /// <param name="answer">What it answered.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Asked {Executable} for the user agent it sends hidden and could not read one from its answer ({Answer}), so a hidden launch keeps the browser's own user agent, which names HeadlessChrome. It is not asked again in this process.")]
    public static partial void NotDerived(ILogger logger, string executable, string answer);

    /// <summary>The ask did not finish inside its bound.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="executable">The browser.</param>
    /// <param name="bound">The bound.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Asking {Executable} for its user agent did not finish in {Bound}, so it was ended and a hidden launch keeps the browser's own user agent.")]
    public static partial void AskHung(ILogger logger, string executable, TimeSpan bound);

    /// <summary>The ask could not be started or read.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="executable">The browser.</param>
    /// <param name="reason">What failed.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Asking {Executable} for its user agent failed ({Reason}), so a hidden launch keeps the browser's own user agent.")]
    public static partial void AskFailed(ILogger logger, string executable, string reason);

    /// <summary>The answer could not be kept beside the browser.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="file">The file.</param>
    /// <param name="reason">What failed.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "The headed user agent could not be kept in {File} ({Reason}); this launch uses it and the next process asks the browser again.")]
    public static partial void NotKept(ILogger logger, string file, string reason);

    /// <summary>The browser is not installed yet, so there is nothing to ask.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="browser">The family.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "{Browser} is not installed yet, so this hidden launch keeps the browser's own user agent, which names HeadlessChrome; a resume once it is installed sends the headed one.")]
    public static partial void NotInstalledYet(ILogger logger, string browser);
}

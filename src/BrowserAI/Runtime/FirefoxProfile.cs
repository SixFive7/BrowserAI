// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using BrowserAI.Interop;
using BrowserAI.Sessions;

namespace BrowserAI.Runtime;

/// <summary>
/// Firefox's profile lock: the preflight that refuses a launch into a collision,
/// and the attribution that says which process is on a profile.
/// </summary>
/// <remarks>
/// <para>
/// <b>The preflight is mandatory rather than defence in depth.</b> Playwright's
/// <c>isProfileLocked</c> checks only Chromium's <c>lockfile</c> and never
/// Firefox's <c>parent.lock</c>, so a collision is not refused anywhere upstream
/// — Firefox raises a <b>native modal on the Windows desktop</b> and the launch
/// blocks against Playwright's three-minute launch timeout. On a background MCP
/// server with nobody at the keyboard that is an invisible hang, which is the
/// founding failure shape of this project rather than an inconvenience.
/// </para>
/// <para>
/// <b>BrowserAI's own session lock is taken before any child starts, so the
/// collision is already unreachable by ordering — and that is exactly why this
/// exists.</b> Coverage by ordering is a guarantee no test states and no
/// refactor notices losing. The preflight says it out loud, in the one function
/// every child launch passes through, and a test holds the lock and watches it
/// fire.
/// </para>
/// <para>
/// <b>Existence proves nothing; only a sharing violation does.</b> Chromium's
/// <c>lockfile</c> is opened <c>FILE_FLAG_DELETE_ON_CLOSE</c>, so the kernel
/// removes it however the browser dies and its presence is liveness. Firefox
/// keeps <c>parent.lock</c> deliberately — it reads the mtime to detect a startup
/// crash — so a profile that has ever been used has one, and a check on
/// existence would refuse every second launch of a healthy session.
/// </para>
/// </remarks>
internal static class FirefoxProfile
{
    /// <summary>
    /// Firefox's profile lock file, as it is spelled on Windows.
    /// </summary>
    /// <remarks>
    /// Windows only. The same lock is <c>.parentlock</c> on Unix, and this
    /// product is Windows-only by charter, so the two spellings never both apply.
    /// </remarks>
    public const string LockFileName = "parent.lock";

    /// <summary>
    /// The preference that stops Firefox registering itself with Windows for
    /// restart after a reboot or an update.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one place browser resurrection can be prevented outright rather
    /// than cleaned up after.</b> Firefox calls
    /// <c>RegisterApplicationRestart</c> in <c>nsAppRunner.cpp</c> with the
    /// original argv, so <c>-profile &lt;dir&gt;</c> survives into whatever
    /// Windows relaunches — and it observes this pref at runtime, calling
    /// <c>UnregisterApplicationRestart</c> when it is false.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is load-bearing for Firefox and would be pointless for
    /// Chromium, and the difference was measured rather than assumed.</b>
    /// Chromium's registration fails on length — Playwright's command line
    /// overshoots the 1023-character limit — so a live Chromium answers
    /// <c>ERROR_NOT_FOUND</c> with nothing set. Firefox's registration does not
    /// go through that call site at all: measured 2026-08-16 against a Firefox
    /// BrowserAI provisioned, exactly one process in the tree answered
    /// <c>S_OK</c>. See [kb](../../../kb/chromium/resurrection.md).
    /// </para>
    /// </remarks>
    public const string RestartRegistrationPreference = "toolkit.winRegisterApplicationRestart";

    /// <summary>Where a profile directory's lock file is.</summary>
    /// <param name="profileDirectory">The profile directory — Playwright's <c>userDataDir</c>.</param>
    /// <returns>The absolute path of <c>parent.lock</c> inside it.</returns>
    public static string LockFileIn(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        return Path.Combine(profileDirectory, LockFileName);
    }

    /// <summary>
    /// Whether a Firefox may be launched on this profile, asked <b>before</b>
    /// anything starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The open is for write and shares everything.</b> Firefox holds
    /// <c>parent.lock</c> with no sharing at all, so any open of ours is refused
    /// while it lives — which means our own share mode decides nothing about the
    /// answer and everything about the damage. Sharing freely makes the
    /// microsecond this handle exists invisible to a second BrowserAI performing
    /// the same preflight; asking for exclusivity would make two harmless
    /// preflights refuse each other.
    /// </para>
    /// <para>
    /// <b>Nothing is created and nothing is written.</b> <c>FileMode.Open</c>
    /// with no truncation: an absent file is <i>free</i>, and a profile that has
    /// never been used must not gain a lock file because BrowserAI looked at it.
    /// </para>
    /// <para>
    /// <b>An open that fails for any other reason is <i>not</i> free.</b> A
    /// denied ACL, a path that cannot be reached, a volume that vanished — none
    /// of them prove the profile is available, and the failure this guards
    /// against costs three minutes of silence. The result says which case it is
    /// so the refusal can too.
    /// </para>
    /// </remarks>
    /// <param name="profileDirectory">The profile directory — Playwright's <c>userDataDir</c>.</param>
    /// <returns>What the lock says.</returns>
    public static FirefoxProfileState Inspect(string profileDirectory)
    {
        var lockFile = LockFileIn(profileDirectory);

        try
        {
            using var probe = new FileStream(
                lockFile,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1);

            return new FirefoxProfileState(FirefoxProfileLockState.Free, lockFile, []);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            // No lock file at all: a profile Firefox has never opened. Firefox
            // never removes one, so this is a genuinely fresh directory.
            return new FirefoxProfileState(FirefoxProfileLockState.Free, lockFile, []);
        }
        catch (IOException failure) when (IsSharingViolation(failure))
        {
            return new FirefoxProfileState(
                FirefoxProfileLockState.Held,
                lockFile,
                Holders(lockFile, out var why),
                why);
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or IOException)
        {
            return new FirefoxProfileState(
                FirefoxProfileLockState.Unknown,
                lockFile,
                [],
                failure.Message);
        }
    }

    /// <summary>
    /// Which live processes hold a profile's <c>parent.lock</c> open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The Firefox half of attribution, and the only half that differs from
    /// Chromium's.</b> Detection — is a browser out of BrowserAI's own tree
    /// running at all — is a full-image-path match and covers both families for
    /// free. What Chromium answers with a message window's title, Firefox answers
    /// only here.
    /// </para>
    /// <para>
    /// ⚠️ <b>This names holders; it does not decide anything.</b> A caller must
    /// intersect what comes back with processes it has independently established
    /// are running <i>its own</i> binaries, by full image path and creation time.
    /// The Restart Manager will happily name the user's personal Firefox if the
    /// profile handed in is the user's — which is precisely why the result of
    /// this call may never be an input to a termination on its own.
    /// </para>
    /// </remarks>
    /// <param name="profileDirectory">The profile directory to ask about.</param>
    /// <returns>The holders, empty when nothing holds it.</returns>
    /// <exception cref="Win32Exception">The Restart Manager refused the question.</exception>
    public static IReadOnlyList<FileHolder> HoldersOf(string profileDirectory) =>
        RestartManager.HoldersOf(LockFileIn(profileDirectory));

    private static IReadOnlyList<FileHolder> Holders(string lockFile, out string? why)
    {
        try
        {
            why = null;
            return RestartManager.HoldersOf(lockFile);
        }
        catch (Win32Exception failure)
        {
            // The refusal stands either way -- the sharing violation is what
            // decided it, and this only names who. A caller is told that the
            // holder could not be identified rather than being told there isn't
            // one.
            why = failure.Message;
            return [];
        }
    }

    private static bool IsSharingViolation(IOException failure) =>
        (failure.HResult & 0xFFFF) is 32 or 33;
}

/// <summary>What a profile's <c>parent.lock</c> says about launching into it.</summary>
internal enum FirefoxProfileLockState
{
    /// <summary>Nothing holds it. A launch may proceed.</summary>
    Free,

    /// <summary>A live process holds it, so a launch would raise the desktop modal.</summary>
    Held,

    /// <summary>
    /// It could not be examined, which is <b>not</b> the same as free and is
    /// treated as a refusal.
    /// </summary>
    Unknown,
}

/// <summary>The preflight's answer.</summary>
/// <param name="State">Free, held, or unexaminable.</param>
/// <param name="LockFile">The file that was examined.</param>
/// <param name="Holders">Who holds it, when Windows would say.</param>
/// <param name="Why">
/// What went wrong, when something did — either the reason the file could not be
/// examined, or the reason its holder could not be named.
/// </param>
internal sealed record FirefoxProfileState(
    FirefoxProfileLockState State,
    string LockFile,
    IReadOnlyList<FileHolder> Holders,
    string? Why = null)
{
    /// <summary>Whether a Firefox may be launched on this profile.</summary>
    public bool MayLaunch => State is FirefoxProfileLockState.Free;
}

/// <summary>
/// A Firefox launch that was refused because its profile is already open.
/// </summary>
/// <remarks>
/// <para>
/// <b>An exception rather than a returned refusal, because of where the check
/// has to be.</b> The guard belongs in the one function every child launch
/// passes through — which builds launch options and has no way to express "no
/// launch" in its return type — and every other reason that function refuses
/// (an incomplete payload, a relative browsers root) throws as well. Answering
/// with <see langword="null"/> options would leave a caller free to ignore it.
/// </para>
/// <para>
/// <b>The message is the error catalogue's row 11 verbatim</b>, so whatever
/// surfaces it — a session tool's refusal, a log line, a stack trace in the
/// process log — says the same sentence, and the catalogue's census can prove
/// this row is reachable.
/// </para>
/// </remarks>
internal sealed class FirefoxProfileLockedException : Exception
{
    /// <summary>Creates the refusal.</summary>
    /// <param name="message">The catalogue row.</param>
    public FirefoxProfileLockedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the refusal.</summary>
    public FirefoxProfileLockedException()
        : base("A Firefox profile is held by another process.")
    {
    }

    /// <summary>Creates the refusal.</summary>
    /// <param name="message">The catalogue row.</param>
    /// <param name="innerException">What was under it.</param>
    public FirefoxProfileLockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The refusal a generated config earns, or <see langword="null"/> when the
    /// launch may proceed.
    /// </summary>
    /// <remarks>
    /// <b>Chromium is answered without touching the filesystem.</b> The check is
    /// Firefox's alone: Chromium's own single-instance protection refuses a
    /// second full build with a clean error, and BrowserAI's directory lock
    /// covers the headless shell it does not ship.
    /// </remarks>
    /// <param name="config">The config about to be written.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    public static FirefoxProfileLockedException? For(GeneratedConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!BrowserConfiguration.IsFirefox(config.Browser))
        {
            return null;
        }

        var state = FirefoxProfile.Inspect(config.ProfileDirectory);

        return state.MayLaunch
            ? null
            : new FirefoxProfileLockedException(SessionErrors.FirefoxProfileLocked(config.ProfileDirectory, state));
    }
}

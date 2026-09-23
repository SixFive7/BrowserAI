// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Registration;

/// <summary>
/// Which executable a client is pointed at, decided from a path and nothing
/// else.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-09-15 (previously "The whole of this type is a pure
/// function of one string, and that is the point … Nothing here reads the disk,
/// the registry, the locator or the environment").</b> It reads the disk now,
/// twice, and only the disk: it opens the composed sibling and reads eight bytes
/// of its PE header. The half of the old sentence that survives is the half that
/// mattered -- <b>no Velopack call, no registry, no environment</b> -- because
/// every Velopack call throws under <c>dotnet run</c> and under every test host,
/// which is what would make this untestable without an install. A file the suite
/// can write is not that: <c>InstalledLayout</c> constructs both arms of the new
/// refusal out of bytes.
/// </para>
/// <para>
/// ⚠️ <b>The refusal is the feature: never register the execution stub.</b> An
/// installed Velopack layout is <c>&lt;root&gt;\BrowserAI.exe</c> -- a
/// <b>392,704-byte</b> Rust stub compiled
/// <c>#![windows_subsystem = "windows"]</c> -- beside
/// <c>&lt;root&gt;\current\BrowserAI.exe</c>, the <b>17,853,952-byte</b> binary
/// that actually serves stdio
/// ([kb](../../../kb/packaging/velopack.md#install--update--rollback-end-to-end)).
/// The stub <b>exits in 59 ms</b> while the app it launched runs on
/// ([kb](../../../kb/packaging/velopack.md#3-never-register-the-execution-stub)), so
/// a client registered against it sees its MCP server die instantly. That is §G
/// landmine 3, and it is the reason this type refuses a path whose parent
/// directory is not <c>current</c> rather than merely preferring one that is.
/// </para>
/// <para>
/// <b>Why the image path is still the input, and what it now buys.</b> Velopack
/// invokes its fast-exit hooks on <c>--mainExe</c> and on nothing else, so inside
/// a hook <see cref="Environment.ProcessPath"/> is <c>&lt;root&gt;\current\BrowserAI.exe</c>
/// -- the <b>configuration app</b>. That is the path that says which install this
/// is; it is not the path a client may be given. The stub never runs a hook, so
/// the shape check below is a guard against a future caller rather than against
/// Velopack.
/// </para>
/// <para>
/// ⚠️ <b>THE GUARANTEE CHANGED, 2026-09-15 (previously "Reading it there means
/// the registered path and the running binary cannot disagree: they are the same
/// string").</b> They are two strings now, and they can disagree, so two checks
/// replace the identity that used to make disagreement unexpressible: the
/// composed sibling <b>must exist</b>, and its PE optional header <b>must
/// declare the console subsystem</b> (<see cref="Runtime.PeSubsystem"/>). Either
/// failing is a refusal naming the file, which reaches the process log and
/// <c>mcp-registration.json</c> through
/// <see cref="McpRegistrar"/>'s refused path.
/// </para>
/// <para>
/// <b>Why a name check would not have been enough.</b> A file called
/// <c>BrowserAI.Server.exe</c> that is really the configuration app -- a
/// mispacked release, a copy somebody made, a rename -- passes every check an
/// extension can make and fails at the worst possible moment: a client starts it
/// expecting stdio, a window appears on the user's screen, and the client waits
/// for a handshake that a dialog is never going to send. The subsystem is a
/// field the linker writes and the loader obeys, and it is the one property a
/// rename cannot forge.
/// </para>
/// </remarks>
internal sealed record RegistrationTarget
{
    /// <summary>
    /// The directory an installed BrowserAI runs out of, and the only one a
    /// client may be pointed into.
    /// </summary>
    public const string CurrentDirectoryName = "current";

    /// <summary>
    /// The Velopack main executable: the configuration app, which is what the
    /// Start Menu points at and what every hook runs as.
    /// </summary>
    public const string AppFileName = "BrowserAI.exe";

    /// <summary>
    /// The MCP server, which is what a client is actually given.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>It was <see cref="AppFileName"/> until 2026-09-15.</b> The names
    /// swapped when the product became two binaries: the configuration app took
    /// <c>BrowserAI.exe</c> because Velopack derives the root stub, the Start
    /// Menu entry, the icon and all four hook invocations from
    /// <c>--mainExe</c> and from nothing else, and the server took a name of its
    /// own. Every registration written before that day names the old path, which
    /// is why the update hook repairs an entry of ours that points at a file
    /// that is no longer there.
    /// </remarks>
    public const string ServerFileName = "BrowserAI.Server.exe";

    /// <summary>The executable a client is given, absolute.</summary>
    public required string Command { get; init; }

    /// <summary>
    /// The install root -- the directory <b>containing</b> <c>current\</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-09-15 (previously "which is where everything that
    /// outlives an update lives").</b> Nothing that outlives an update lives
    /// there any more, and nothing may: <c>Setup.exe</c> renames a non-empty
    /// install root aside and deletes it, and uninstall empties it. State lives
    /// in the data root (<see cref="Hosting.IAppPaths"/>), which is a sibling.
    /// What this property is <i>for</i> is unchanged and is smaller than the old
    /// sentence claimed: it is the second half of the one judgement this type
    /// makes about an image path, so a caller that resolved a command can say
    /// which install it came out of without splitting the string again.
    /// </remarks>
    public required string InstallRoot { get; init; }

    /// <summary>
    /// Decides what to register from the running image's path.
    /// </summary>
    /// <param name="imagePath">
    /// The path of the binary being asked about, normally
    /// <see cref="Environment.ProcessPath"/> inside a Velopack hook.
    /// </param>
    /// <param name="target">The target, when there is one.</param>
    /// <param name="refusal">
    /// Why there is not, as a sentence naming the path and what was expected.
    /// Empty when <paramref name="target"/> is set.
    /// </param>
    /// <returns>Whether a client may be pointed at this path.</returns>
    public static bool TryResolve(string? imagePath, out RegistrationTarget? target, out string refusal)
    {
        target = null;

        if (imagePath is not { Length: > 0 })
        {
            refusal = "The running image has no path, so there is nothing to register. BrowserAI composes the server's path from the directory it is itself running out of, and a process that cannot name its own image has no directory to compose from.";
            return false;
        }

        if (!Path.IsPathFullyQualified(imagePath))
        {
            refusal = $"'{imagePath}' is not a fully qualified path. A registered command is resolved by the client, in whatever working directory the client happens to have, so a relative one would name a different file on every launch -- or none.";
            return false;
        }

        var directory = Path.GetDirectoryName(imagePath);

        if (directory is not { Length: > 0 })
        {
            refusal = $"'{imagePath}' has no parent directory, so it cannot be an installed BrowserAI.";
            return false;
        }

        var directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!string.Equals(directoryName, CurrentDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            // The one refusal that exists to stop a specific 392,704-byte file
            // from reaching a client's configuration.
            refusal = $"'{imagePath}' is not inside a '{CurrentDirectoryName}' directory, so it is not the binary an installed BrowserAI serves stdio from. The execution stub sits beside that directory, is compiled as a Windows-subsystem binary and exits in 59 ms without waiting -- a client registered against it sees its MCP server die at the handshake. Nothing is registered.";
            return false;
        }

        var root = Path.GetDirectoryName(directory);

        if (root is not { Length: > 0 })
        {
            refusal = $@"'{imagePath}' is inside a '{CurrentDirectoryName}' directory with no parent, so it is not an installed layout: an installed BrowserAI runs out of '<install root>\{CurrentDirectoryName}\{AppFileName}'.";
            return false;
        }

        // ⚠️ THE SIBLING, COMPOSED AND THEN CHECKED. Everything above this line
        // is about the path of the process that is ASKING; everything below is
        // about the file a client would be handed, which is a different file
        // from this day on.
        var server = Path.Combine(directory, ServerFileName);

        if (!File.Exists(server))
        {
            refusal = $"'{server}' is not there. BrowserAI registers its MCP server, '{ServerFileName}', which ships beside the configuration app that runs the installer's hooks -- and this install has the app without the server. Nothing is registered: a client pointed at a file that does not exist reports a server that will not start, with nothing to say which file was missing. Reinstall BrowserAI, or run the installer again over this root.";
            return false;
        }

        var subsystem = Runtime.PeSubsystem.Of(server);

        if (subsystem is not Runtime.PeSubsystem.WindowsCui)
        {
            refusal = $"'{server}' is {Runtime.PeSubsystem.Describe(subsystem)}, and BrowserAI's MCP server is a console-subsystem binary because a client speaks to it over stdio. A file of this kind at that name is either a mispacked release or somebody's copy of '{AppFileName}' wearing the server's name -- and registering it would put a window on the screen at every session start while the client waited forever for a handshake. Nothing is registered.";
            return false;
        }

        target = new RegistrationTarget { Command = server, InstallRoot = root };
        refusal = string.Empty;
        return true;
    }
}

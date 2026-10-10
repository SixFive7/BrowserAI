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
/// function of one string, and that is the point ... Nothing here reads the disk,
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
/// directory is not <c>current</c> instead of merely preferring one that is.
/// </para>
/// <para>
/// <b>Why the image path is still the input, and what it now buys.</b> Velopack
/// invokes its fast-exit hooks on <c>--mainExe</c> and on nothing else, so inside
/// a hook <see cref="Environment.ProcessPath"/> is <c>&lt;root&gt;\current\BrowserAI.exe</c>
/// -- the <b>configuration app</b>. That is the path that says which install this
/// is; it is not the path a client may be given. The stub never runs a hook, so
/// the shape check below is a guard against a future caller and not against
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
/// <para>
/// ⚠️ <b>ONE FILE AGAIN SINCE 2026-10-08, D7 a, the maintainer's words verbatim:
/// <i>"d7 a"</i></b> (previously the composed sibling was
/// <c>current\BrowserAI.Server.exe</c> and had to declare the CONSOLE subsystem).
/// A client is registered as <c>current\BrowserAI.exe --mcp</c>, the file every
/// hook runs as, and the check turned round: it must declare the WINDOWS
/// subsystem, because the one executable is windowless by construction and a
/// console file at that name, an old build or a mispacked one, would be given a
/// console window by every windowless client that starts it. The two checks stay
/// two -- the file exists, and its header says what it is -- because the image
/// asking is not always the file registered: the suite asks about installs it
/// composes.
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
    /// The one executable: the Velopack main executable, what the Start Menu
    /// points at, what every hook runs as, and what a client is given.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-08 (previously "The Velopack main executable: the
    /// configuration app, which is what the Start Menu points at and what every
    /// hook runs as"), D7 a.</i>
    /// </remarks>
    public const string AppFileName = "BrowserAI.exe";

    /// <summary>
    /// The MCP server's file from 2026-09-15 to 2026-10-08, which no build ships
    /// any more.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Kept because registrations still name it.</b> Every user-scope entry
    /// written in that time names <c>current\BrowserAI.Server.exe</c>; the update
    /// hook registers <c>current\BrowserAI.exe --mcp</c>, and RegisterAI replaces an
    /// entry of ours that names a different file (its README, <i>register</i>), so
    /// the user-scope entries move on the first update. A project file that names
    /// it breaks once, which D7 a accepted. <i>Previously <c>ServerFileName</c>, the
    /// file a client was given.</i>
    /// </remarks>
    public const string RetiredServerFileName = "BrowserAI.Server.exe";

    /// <summary>The argument that makes the one executable serve a client over stdio.</summary>
    /// <remarks>
    /// <b>The registration carries it</b>: RegisterAI passes everything after
    /// <c>--</c> to the client unchanged (its README), and a start with no argument
    /// is a person's.
    /// </remarks>
    public const string McpArgument = "--mcp";

    /// <summary>The executable a client is given, absolute.</summary>
    public required string Command { get; init; }

    /// <summary>What a client passes the executable: <see cref="McpArgument"/>, and nothing else today.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [McpArgument];

    /// <summary>The command and its arguments, one element each, the way a registration writes them.</summary>
    public IReadOnlyList<string> CommandLine => [Command, .. Arguments];

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

        // ⚠️ THE FILE, COMPOSED AND THEN CHECKED. Everything above this line is
        // about the path of the process that is ASKING; everything below is about
        // the file a client would be handed. Since 2026-10-08 the two are one file
        // in an install, and a caller may still ask about a layout it composed.
        var server = Path.Combine(directory, AppFileName);

        if (!File.Exists(server))
        {
            // The texts polish, 2026-10-10, page #164 (previously "BrowserAI registers
            // 'BrowserAI.exe --mcp' from ..." and "Reinstall BrowserAI, or run the installer
            // again over this root."): one action, and a registration does not always carry
            // --mcp alone.
            refusal = $"'{server}' is not there. BrowserAI registers the {AppFileName} in the folder its hooks run in, and this install has no such file. Nothing is registered: a client pointed at a missing file reports a server that will not start, without saying which file is missing. Reinstall BrowserAI.";
            return false;
        }

        var subsystem = Runtime.PeSubsystem.Of(server);

        if (subsystem is not Runtime.PeSubsystem.WindowsGui)
        {
            // The texts polish, 2026-10-10, page #165 (previously "'<file>' is no readable
            // PE header at all, ...", not a sentence, and "no starter"). Its own words, since
            // PeSubsystem.Describe has other callers.
            var kind = subsystem switch
            {
                null => "it has no readable PE header",
                Runtime.PeSubsystem.WindowsCui => "it is a console-subsystem binary (3)",
                _ => $"its subsystem is {subsystem.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            };

            refusal = $"'{server}' is not a Windows-subsystem file: {kind}. BrowserAI is one Windows-subsystem file, so that nothing that starts it, a client included, gives it a console window. A file of another kind at that name is a mispacked release or an older build, and registering it would put a console window on the screen at every session start. Nothing is registered.";
            return false;
        }

        target = new RegistrationTarget { Command = server, InstallRoot = root };
        refusal = string.Empty;
        return true;
    }
}

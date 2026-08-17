// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Registration;

/// <summary>
/// Which executable a client is pointed at, decided from a path and nothing
/// else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole of this type is a pure function of one string, and that is the
/// point.</b> Every Velopack call throws under <c>dotnet run</c> and under every
/// test host, so a decision that consulted <c>VelopackLocator</c> could not be
/// tested without an install — the same reasoning that produced
/// <see cref="Hosting.IAppPaths"/>. Nothing here reads the disk, the registry,
/// the locator or the environment.
/// </para>
/// <para>
/// ⚠️ <b>The refusal is the feature: never register the execution stub.</b> An
/// installed Velopack layout is <c>&lt;root&gt;\BrowserAI.exe</c> — a
/// <b>392,704-byte</b> Rust stub compiled
/// <c>#![windows_subsystem = "windows"]</c> — beside
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
/// <b>Why the image path is the input.</b> Velopack invokes its fast-exit hooks
/// on <c>--mainExe</c>, which this project packs as <c>BrowserAI.exe</c> inside
/// <c>current\</c> — so inside a hook <see cref="Environment.ProcessPath"/>
/// <i>is</i> the path a client must be given. Reading it there means the
/// registered path and the running binary cannot disagree: they are the same
/// string. The stub never runs a hook, so the shape check below is a guard
/// against a future caller rather than against Velopack.
/// </para>
/// </remarks>
internal sealed record RegistrationTarget
{
    /// <summary>
    /// The directory an installed BrowserAI runs out of, and the only one a
    /// client may be pointed into.
    /// </summary>
    public const string CurrentDirectoryName = "current";

    /// <summary>The executable a client is given, absolute.</summary>
    public required string Command { get; init; }

    /// <summary>
    /// The install root — the directory <b>containing</b> <c>current\</c>, which
    /// is where everything that outlives an update lives.
    /// </summary>
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
            refusal = "The running image has no path, so there is nothing to register. BrowserAI registers the executable it is itself running from, and a process that cannot name its own image cannot be pointed at.";
            return false;
        }

        if (!Path.IsPathFullyQualified(imagePath))
        {
            refusal = $"'{imagePath}' is not a fully qualified path. A registered command is resolved by the client, in whatever working directory the client happens to have, so a relative one would name a different file on every launch — or none.";
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
            refusal = $"'{imagePath}' is not inside a '{CurrentDirectoryName}' directory, so it is not the binary an installed BrowserAI serves stdio from. The execution stub sits beside that directory, is compiled as a Windows-subsystem binary and exits in 59 ms without waiting — a client registered against it sees its MCP server die at the handshake. Nothing is registered.";
            return false;
        }

        var root = Path.GetDirectoryName(directory);

        if (root is not { Length: > 0 })
        {
            refusal = $"'{imagePath}' is inside a '{CurrentDirectoryName}' directory with no parent, so there is no install root beside it to hold the log and the registration record.";
            return false;
        }

        target = new RegistrationTarget { Command = imagePath, InstallRoot = root };
        refusal = string.Empty;
        return true;
    }
}

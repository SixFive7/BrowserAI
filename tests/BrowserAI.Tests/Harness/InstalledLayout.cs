// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Registration;
using BrowserAI.Runtime;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// An installed BrowserAI's <c>current\</c> directory, built out of bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-09-15, when registration stopped being a pure function of a
/// string.</b> Until that day <see cref="RegistrationTarget"/> registered
/// <c>Environment.ProcessPath</c> verbatim and every arm about it could name an
/// absolute path that existed nowhere. The configuration app runs the hooks now,
/// so the path it registers is its SIBLING -- composed, then checked -- and a
/// composed path that nothing checks is a guess.
/// </para>
/// <para>
/// <b>The files are real PE headers and not real executables</b>, and that is
/// the point, not a shortcut: what
/// <see cref="PeSubsystem"/> reads is eight bytes at three documented offsets, so
/// a test can construct the exact input it wants to assert about -- a console
/// binary, a Windows one, a file that is not a PE at all -- without publishing
/// anything, without a 54 MB fixture, and without the arms depending on a
/// publish that may be stale. The real published binaries are checked separately,
/// by <c>ReleaseScriptTests</c>, against the same reader.
/// </para>
/// </remarks>
internal static class InstalledLayout
{
    /// <summary>
    /// Creates <c>&lt;root&gt;\current\</c> holding both executables.
    /// </summary>
    /// <param name="root">The install root.</param>
    /// <returns>The configuration app's image path, which is what a hook runs as.</returns>
    public static string Create(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var current = Directory.CreateDirectory(
            Path.Combine(root, RegistrationTarget.CurrentDirectoryName));

        var app = Path.Combine(current.FullName, RegistrationTarget.AppFileName);

        WritePortableExecutable(app, PeSubsystem.WindowsGui);
        WritePortableExecutable(
            Path.Combine(current.FullName, RegistrationTarget.ServerFileName),
            PeSubsystem.WindowsCui);

        return app;
    }

    /// <summary>
    /// Creates <c>&lt;root&gt;\current\</c> holding the app and <b>no</b> server.
    /// </summary>
    /// <param name="root">The install root.</param>
    /// <returns>The configuration app's image path.</returns>
    public static string CreateWithoutTheServer(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var current = Directory.CreateDirectory(
            Path.Combine(root, RegistrationTarget.CurrentDirectoryName));

        var app = Path.Combine(current.FullName, RegistrationTarget.AppFileName);
        WritePortableExecutable(app, PeSubsystem.WindowsGui);

        return app;
    }

    /// <summary>The server's path inside a layout this created.</summary>
    /// <param name="root">The install root.</param>
    /// <returns>The path, whether or not anything is at it.</returns>
    public static string ServerIn(string root) =>
        Path.Combine(root, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.ServerFileName);

    /// <summary>
    /// Writes the smallest file that carries a readable PE optional header
    /// declaring <paramref name="subsystem"/>.
    /// </summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="subsystem">The subsystem value to declare.</param>
    /// <remarks>
    /// <b>PE32+ (<c>0x20B</c>), because that is what a win-x64 publish emits</b>
    /// and because the offset being asserted is the one a real binary would put
    /// it at. The header is zero everywhere it does not have to be: nothing here
    /// is loadable and nothing here is meant to be.
    /// </remarks>
    public static void WritePortableExecutable(string path, int subsystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // The DOS header, the PE signature, the 20-byte COFF header and enough
        // optional header to reach Subsystem at +68 and read two bytes.
        const int PeHeaderAt = 0x80;
        const int OptionalAt = PeHeaderAt + 4 + 20;
        const int SubsystemAt = OptionalAt + 68;

        var bytes = new byte[SubsystemAt + 2];

        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';

        // e_lfanew, little-endian.
        bytes[0x3C] = PeHeaderAt;

        bytes[PeHeaderAt] = (byte)'P';
        bytes[PeHeaderAt + 1] = (byte)'E';

        // The optional header magic: PE32+.
        bytes[OptionalAt] = 0x0B;
        bytes[OptionalAt + 1] = 0x02;

        bytes[SubsystemAt] = (byte)(subsystem & 0xFF);
        bytes[SubsystemAt + 1] = (byte)((subsystem >> 8) & 0xFF);

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Writes a file that is not a portable executable at all.</summary>
    /// <param name="path">Where to write it.</param>
    public static void WriteSomethingThatIsNotAnExecutable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        File.WriteAllText(path, "This is not an executable. It is a text file wearing an executable's name.\n");
    }
}

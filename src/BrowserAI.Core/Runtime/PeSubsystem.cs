// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Runtime;

/// <summary>
/// What kind of executable a file is, read out of its own PE optional header.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-09-15, because a name stopped being enough.</b> BrowserAI shipped
/// two executables from that day until 2026-10-08: <c>BrowserAI.exe</c>, a
/// Windows-subsystem configuration app, and <c>BrowserAI.Server.exe</c>, the
/// console-subsystem MCP server. Since 2026-10-08 (D7 a) it ships one,
/// <c>BrowserAI.exe</c>, windowless, and registration checks that the file a
/// client is given says so. They sit in the same directory, and the one that gets registered with
/// a client is composed from the other one's path -- see
/// <see cref="Registration.RegistrationTarget"/>. A composed path is a guess
/// until something checks it, and the failure a check prevents here is specific:
/// registering the <b>app</b> under the server's name would put a window on the
/// user's screen every time a client started a session, and the client would
/// then wait forever for a JSON-RPC handshake from a process that is showing a
/// dialog.
/// </para>
/// <para>
/// <b>The subsystem is the discriminator that cannot be faked by a rename</b>,
/// which is the whole reason it is read and the file name not trusted. It is
/// a field the linker writes and the loader obeys: subsystem 2 is never given a
/// console and subsystem 3 always is
/// (<i>Learn: windows/console/creation-of-a-console</i>).
/// </para>
/// <para>
/// <b>Nothing here is Win32.</b> The layout is public, fixed and documented, so
/// this is eight bytes read at three offsets and not a P/Invoke -- which also
/// means it works on a file that is not loadable, is the wrong architecture, or
/// is a hand-built header a test wrote, and that last one is what makes the
/// refusals above assertable over constructed inputs.
/// </para>
/// <para>
/// ⚠️ <b>It answers <see langword="null"/> instead of throwing for anything it
/// cannot read</b> -- absent, too short, not a PE, an unreadable handle. The
/// caller is a registration decision inside an installer hook, where the
/// difference between <i>this is the wrong kind of file</i> and <i>this file
/// could not be read</i> does not change what happens: neither may be
/// registered. What it does change is the sentence, so the caller distinguishes
/// them and this does not.
/// </para>
/// </remarks>
internal static class PeSubsystem
{
    /// <summary>A Windows-subsystem binary: the loader never gives it a console.</summary>
    public const int WindowsGui = 2;

    /// <summary>A console-subsystem binary: the loader always gives it one.</summary>
    public const int WindowsCui = 3;

    /// <summary>
    /// Where the PE signature's own offset lives inside the DOS header.
    /// </summary>
    private const int LfaNewOffset = 0x3C;

    /// <summary>
    /// How far the optional header sits past the PE signature: four signature
    /// bytes and a twenty-byte COFF file header.
    /// </summary>
    private const int OptionalHeaderOffset = 4 + 20;

    /// <summary>
    /// Where <c>Subsystem</c> sits inside the optional header.
    /// </summary>
    /// <remarks>
    /// <b>68 for PE32 and PE32+ alike</b>, which is not a coincidence and is
    /// written down because it looks like one: PE32+ widens
    /// <c>ImageBase</c> from four bytes to eight and drops <c>BaseOfData</c>,
    /// which is four bytes, so the two changes cancel and every field after
    /// <c>ImageBase</c> keeps its offset. Checked against the documented layout
    /// in both magics, not measured on one and assumed for the other.
    /// </remarks>
    private const int SubsystemOffset = 68;

    /// <summary>The smallest file that could carry all three reads.</summary>
    private const long SmallestPossible = LfaNewOffset + 4;

    /// <summary>
    /// The subsystem the file at <paramref name="path"/> declares, or
    /// <see langword="null"/> when it does not declare one this can read.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns><see cref="WindowsGui"/>, <see cref="WindowsCui"/>, another
    /// documented subsystem value, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <b>Read asynchronously since 2026-10-11</b>, the maintainer's decision 63, in his
    /// words verbatim: <i>"convert disk writes as well. Some users have slow spinning
    /// rust."</i> <i>Previously <c>Of</c>, over a <see cref="FileStream"/> read synchronously.</i>
    /// </remarks>
    public static async Task<int?> OfAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var file = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.Asynchronous);

            var length = RandomAccess.GetLength(file);

            if (length < SmallestPossible)
            {
                return null;
            }

            // "MZ". Checked before the offset it guards is believed, because
            // `e_lfanew` in a file that is not a PE is four arbitrary bytes and
            // seeking to them lands anywhere.
            if (await ReadUInt16Async(file, 0).ConfigureAwait(false) is not 0x5A4D)
            {
                return null;
            }

            var peHeader = await ReadUInt32Async(file, LfaNewOffset).ConfigureAwait(false);

            if (peHeader is 0 or > (uint)(int.MaxValue - OptionalHeaderOffset - SubsystemOffset - 2))
            {
                return null;
            }

            // "PE\0\0".
            if (await ReadUInt32Async(file, (long)peHeader).ConfigureAwait(false) is not 0x0000_4550)
            {
                return null;
            }

            var optional = (long)peHeader + OptionalHeaderOffset;

            if (length < optional + SubsystemOffset + 2)
            {
                return null;
            }

            // 0x10B is PE32 and 0x20B is PE32+. Anything else is a ROM image or
            // a file this does not understand, and either way the offset below
            // is not where Subsystem is.
            if (await ReadUInt16Async(file, optional).ConfigureAwait(false) is not (0x010B or 0x020B))
            {
                return null;
            }

            return await ReadUInt16Async(file, optional + SubsystemOffset).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A file that cannot be read is a file that may not be registered, which is the same answer as a file of the wrong kind.
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>Whether the file is a console-subsystem executable.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>Whether it declares <see cref="WindowsCui"/>.</returns>
    public static async Task<bool> IsConsoleAsync(string path) => await OfAsync(path).ConfigureAwait(false) is WindowsCui;

    /// <summary>How a subsystem value reads in a refusal.</summary>
    /// <param name="subsystem">What was read, or <see langword="null"/>.</param>
    /// <returns>A phrase naming it.</returns>
    public static string Describe(int? subsystem) => subsystem switch
    {
        null => "no readable PE header at all",
        WindowsGui => "a Windows-subsystem binary (2), which is never given a console",
        WindowsCui => "a console-subsystem binary (3)",
        _ => $"subsystem {subsystem.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
    };

    private static async Task<ushort> ReadUInt16Async(SafeFileHandle file, long offset)
    {
        var buffer = new byte[2];
        await FillAsync(file, buffer, offset).ConfigureAwait(false);
        return (ushort)(buffer[0] | (buffer[1] << 8));
    }

    private static async Task<uint> ReadUInt32Async(SafeFileHandle file, long offset)
    {
        var buffer = new byte[4];
        await FillAsync(file, buffer, offset).ConfigureAwait(false);
        return (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
    }

    /// <summary>
    /// Fills the buffer from an absolute offset, or throws.
    /// </summary>
    /// <remarks>
    /// <b>Read until it is full, never once</b>: a short read at a file boundary would
    /// otherwise leave the tail of the buffer at zero and be indistinguishable from a
    /// field that really is zero -- which for <c>Subsystem</c> is
    /// <c>IMAGE_SUBSYSTEM_UNKNOWN</c>, a value that reads like an answer. <i>Corrected
    /// 2026-10-11 (previously "<c>ReadExactly</c>, not <c>Read</c>"), when the reads became
    /// asynchronous reads at an offset.</i>
    /// </remarks>
    private static async Task FillAsync(SafeFileHandle file, Memory<byte> buffer, long offset)
    {
        while (!buffer.IsEmpty)
        {
            var read = await RandomAccess.ReadAsync(file, buffer, offset).ConfigureAwait(false);

            if (read is 0)
            {
                throw new EndOfStreamException();
            }

            buffer = buffer[read..];
            offset += read;
        }
    }
}

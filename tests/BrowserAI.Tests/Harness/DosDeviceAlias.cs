// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A real drive letter, defined for this logon session, standing for something
/// it is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are the genuine article rather than a stand-in, and that is the
/// whole point of the file.</b> <c>DefineDosDeviceW</c> is what <c>subst</c>
/// calls, and with <c>DDD_RAW_TARGET_PATH</c> it writes the same kind of object
/// manager symbolic link that the multiple-UNC provider writes for
/// <c>net use</c>. Measured 2026-08-19 on this machine: a letter defined here
/// against <c>\Device\LanmanRedirector\…</c> is reported <c>DRIVE_REMOTE</c> by
/// <c>GetDriveTypeW</c>, and a <c>File.Exists</c> through it against a dead
/// hostname took <b>22,210 ms</b> — indistinguishable from a mapping made by
/// <c>net use</c>, because it is the same object.
/// </para>
/// <para>
/// <b>Neither form needs administrator rights</b>, which is what makes the
/// mapped-drive half of the network refusal testable at all. An unelevated
/// <c>DefineDosDevice</c> writes into this logon session's own DosDevices
/// directory, so the letter exists for this desktop and vanishes with
/// <see cref="Dispose"/>. The one thing it does <i>not</i> do is establish an
/// SMB session — nothing here talks to a server — and nothing in these tests
/// wants one: the product's whole claim is that it decides before any of that.
/// </para>
/// <para>
/// <b>Letters are allocated upward from E: while
/// <c>SessionIndexTests.FirstUnmountedDriveLetter</c> searches downward from
/// Z:.</b> Both run unbounded-parallel against the same twenty-two letters, and
/// opposite directions is what keeps them apart without either taking a lock the
/// other knows about.
/// </para>
/// </remarks>
internal sealed partial class DosDeviceAlias : IDisposable
{
    private const uint RawTargetPath = 0x00000001;
    private const uint RemoveDefinition = 0x00000002;

    private static readonly Lock Gate = new();

    private bool _removed;

    private DosDeviceAlias(string letter, string target)
    {
        Letter = letter;
        Target = target;
    }

    /// <summary>The allocated device name, <c>X:</c>, with no trailing separator.</summary>
    public string Letter { get; }

    /// <summary>The object manager target the letter was defined against.</summary>
    public string Target { get; }

    /// <summary>The root path, <c>X:\</c>.</summary>
    public string Root => Letter + @"\";

    /// <summary>
    /// A <c>subst</c>: a drive letter that stands for a directory on another
    /// drive.
    /// </summary>
    /// <param name="directory">The local directory the letter stands for.</param>
    /// <returns>The alias, removed on <see cref="Dispose"/>.</returns>
    public static DosDeviceAlias Substituting(string directory) => Define(directory, raw: false);

    /// <summary>
    /// A mapped network drive: a drive letter that resolves through the SMB
    /// redirector.
    /// </summary>
    /// <param name="hostAndShare">
    /// The <c>host\share</c> half of the UNC path. <b>Point it at something that
    /// fails fast</b> — these tests prove that nothing reaches it, so a dead
    /// hostname would only cost the suite twenty-two seconds if a test ever
    /// regressed into touching it.
    /// </param>
    /// <returns>The alias, removed on <see cref="Dispose"/>.</returns>
    public static DosDeviceAlias MappedTo(string hostAndShare) =>
        Define($@"\Device\LanmanRedirector\;{{letter}}0000000000012345\{hostAndShare}", raw: true);

    /// <summary>A path beneath this letter.</summary>
    /// <param name="relative">The part after the root.</param>
    /// <returns>The full path.</returns>
    public string PathTo(string relative) => Root + relative;

    /// <inheritdoc />
    public void Dispose()
    {
        lock (Gate)
        {
            if (_removed)
            {
                return;
            }

            // IntPtr.Zero rather than a null string: measured 2026-08-19, a null
            // marshalled from a `string` parameter left the definition standing
            // and still returned TRUE, so the letter survived the test that made
            // it. The removal is asserted below rather than assumed for the same
            // reason -- a leaked drive letter is a machine-wide side effect, and
            // the next run would meet it as a mystery.
            _ = DefineDosDeviceW(RemoveDefinition, Letter, IntPtr.Zero);
            _removed = true;
        }

        if (TargetOf(Letter) is { } surviving)
        {
            throw new InvalidOperationException(
                $"The alias '{Letter}' -> '{Target}' could not be removed and is still defined as '{surviving}'. Remove it before running this suite again: subst {Letter} /d.");
        }
    }

    private static DosDeviceAlias Define(string target, bool raw)
    {
        lock (Gate)
        {
            foreach (var candidate in "EFGHIJKLMNOPQRSTUVWXYZ")
            {
                var letter = $"{candidate}:";

                if (TargetOf(letter) is not null)
                {
                    continue;
                }

                var resolved = target.Replace("{letter}", letter, StringComparison.Ordinal);

                if (!DefineDosDeviceW(raw ? RawTargetPath : 0, letter, resolved))
                {
                    throw new InvalidOperationException(
                        $"DefineDosDeviceW could not define '{letter}' as '{resolved}' (error {Marshal.GetLastPInvokeError()}). This test proves nothing without a real alias.");
                }

                // Asserted rather than assumed: the whole value of this helper is
                // that the alias is the same object Windows makes, and a letter
                // that came back holding something else would make every
                // assertion downstream vacuous.
                //
                // A NON-RAW definition reads back with `\??\` in front of it,
                // which is the object manager's prefix for a DOS path and is
                // exactly the discriminator VolumeIdentity keys the `subst`
                // answer off. Measured 2026-08-19, and stated here because
                // comparing against the string that was passed in fails.
                var stored = raw ? resolved : @"\??\" + resolved;
                var actual = TargetOf(letter);

                return string.Equals(actual, stored, StringComparison.Ordinal)
                    ? new DosDeviceAlias(letter, stored)
                    : throw new InvalidOperationException(
                        $"'{letter}' was defined as '{resolved}' and reads back as '{actual ?? "(nothing)"}' rather than as '{stored}'.");
            }
        }

        throw new InvalidOperationException(
            "Every drive letter from E: to Z: is already defined, so a drive-letter alias cannot be created on this machine.");
    }

    private static unsafe string? TargetOf(string letter)
    {
        const int Capacity = 1024;
        var buffer = stackalloc char[Capacity];

        return QueryDosDeviceW(letter, buffer, Capacity) is 0 ? null : new string(buffer);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DefineDosDeviceW(uint dwFlags, string lpDeviceName, string lpTargetPath);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DefineDosDeviceW(uint dwFlags, string lpDeviceName, IntPtr lpTargetPath);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint QueryDosDeviceW(string lpDeviceName, char* lpTargetPath, uint ucchMax);
}

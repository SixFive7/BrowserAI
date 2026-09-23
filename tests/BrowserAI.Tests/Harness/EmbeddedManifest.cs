// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The <c>RT_MANIFEST</c> resource of a built binary, as text.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The manifest is the reason the configuration app has a window at
/// all.</b> <c>TaskDialogIndirect</c> lives in the side-by-side version 6
/// <c>comctl32</c>, which a process does <b>not</b> get by default: without the
/// <c>Microsoft.Windows.Common-Controls</c> 6.0.0.0 dependency the loader binds
/// version 5, the export is absent, and the call fails at run time with no
/// compile-time signal of any kind. It presents as <i>the app starts and nothing
/// happens</i> -- which is the hardest shape of failure to diagnose and the one
/// nothing in this repository could see until 2026-09-16.
/// </para>
/// <para>
/// <b>Read out of the built file rather than out of <c>app.manifest</c>.</b> The
/// source file is what a person edits; what matters is what the SDK embedded,
/// and a project that stopped carrying <c>ApplicationManifest</c> would leave the
/// source file sitting in the tree saying the right thing about a binary that no
/// longer carries it.
/// </para>
/// <para>
/// <b>Hand-written P/Invoke, like every other harness that talks to Windows.</b>
/// <c>NativeMethods.txt</c> is deliberately structs-only -- a generated function
/// there would be a second way to call Windows living in the test assembly -- so
/// the four resource calls are declared here.
/// <c>LOAD_LIBRARY_AS_IMAGE_RESOURCE</c> maps the file for its resources alone:
/// nothing is relocated, no entry point runs, and it works on a binary for
/// another architecture.
/// </para>
/// </remarks>
internal static partial class EmbeddedManifest
{
    /// <summary><c>RT_MANIFEST</c>.</summary>
    private const int ManifestType = 24;

    /// <summary>
    /// <c>CREATEPROCESS_MANIFEST_RESOURCE_ID</c>: the id an executable's own
    /// manifest is written under.
    /// </summary>
    private const int ExecutableManifestId = 1;

    /// <summary>Map it for resources; do not relocate it and do not run it.</summary>
    private const uint LoadLibraryAsImageResource = 0x0000_0020;

    /// <summary>Do not call <c>DllMain</c> and do not resolve imports.</summary>
    private const uint LoadLibraryAsDatafileExclusive = 0x0000_0040;

    /// <summary>
    /// The manifest embedded in a binary at the executable manifest id.
    /// </summary>
    /// <param name="path">The binary.</param>
    /// <returns>Its manifest, or <see langword="null"/> when it carries none.</returns>
    public static string? Of(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var module = LoadLibraryExW(path, 0, LoadLibraryAsImageResource | LoadLibraryAsDatafileExclusive);

        if (module == nint.Zero)
        {
            throw new InvalidOperationException(
                $"'{path}' could not be mapped for its resources: {Marshal.GetLastPInvokeError()}. "
                + "That is a file that is not there or is not a PE image, either of which is a defect rather than a reason to skip.");
        }

        try
        {
            var found = FindResourceW(module, ExecutableManifestId, ManifestType);

            if (found == nint.Zero)
            {
                return null;
            }

            var size = SizeofResource(module, found);
            var loaded = LoadResource(module, found);

            if (size is 0 || loaded == nint.Zero)
            {
                return null;
            }

            var bytes = LockResource(loaded);

            if (bytes == nint.Zero)
            {
                return null;
            }

            var copy = new byte[size];

            Marshal.Copy(bytes, copy, 0, (int)size);

            // A manifest is UTF-8 and may carry a BOM; both are ordinary.
            return Encoding.UTF8.GetString(copy).TrimStart('﻿');
        }
        finally
        {
            _ = FreeLibrary(module);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LoadLibraryExW(string fileName, nint file, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "FindResourceW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint FindResourceW(nint module, nint name, nint type);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SizeofResource(nint module, nint resource);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LoadResource(nint module, nint resource);

    [LibraryImport("kernel32.dll", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LockResource(nint resourceData);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint module);
}

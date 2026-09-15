// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.App.Interop;

/// <summary>
/// The shell calls: pick a folder, and open one.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b><c>SHBrowseForFolderW</c> rather than <c>IFileOpenDialog</c>, and this
/// is a decision rather than an oversight.</b> The modern picker is COM, and
/// COM under NativeAOT means source-generated interop —
/// <c>[GeneratedComInterface]</c> over <c>IModalWindow</c>,
/// <c>IFileDialog</c> and <c>IFileOpenDialog</c>, which is twenty-six vtable
/// slots that have to be declared in exact order and whose only failure mode is
/// calling the wrong function at run time with no diagnostic. That is a real
/// risk to take for a folder picker, and it is a risk this repository cannot
/// retire the way it retires others: <b>no test here can open a modal window</b>,
/// so a wrong slot would be found by the maintainer clicking a button rather
/// than by a run.
/// </para>
/// <para>
/// <b>What is given up is small and is stated rather than glossed.</b>
/// <c>BIF_NEWDIALOGSTYLE</c> gives a resizable dialog with a tree, a
/// <i>New folder</i> button and drag-and-drop; what it does not give is the
/// Explorer-style navigation pane, the address bar and the places list of the
/// modern picker. Both return one directory and both are cancellable. This is
/// the supported, documented API, it is pure P/Invoke, and it is AOT-clean with
/// no declarations whose order matters.
/// </para>
/// <para>
/// ⚠️ <b><c>BIF_NEWDIALOGSTYLE</c> requires the thread to be in a
/// single-threaded apartment</b>, and a thread that is not gets the old dialog
/// with no error. That is why the entry point is <c>[STAThread]</c> — a fact
/// that is invisible at the call site and is therefore written down at both
/// ends.
/// </para>
/// </remarks>
internal static partial class ShellInterop
{
    /// <summary>Only file-system directories may be chosen.</summary>
    private const uint ReturnOnlyFileSystemDirectories = 0x0001;

    /// <summary>The resizable dialog with a New folder button. Needs an STA thread.</summary>
    private const uint NewDialogStyle = 0x0040;

    /// <summary>
    /// Asks for a directory.
    /// </summary>
    /// <param name="owner">The window the dialog is modal to, or zero.</param>
    /// <param name="prompt">The sentence above the tree.</param>
    /// <returns>The directory, or <see langword="null"/> when cancelled.</returns>
    public static string? PickFolder(nint owner, string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var title = Marshal.StringToHGlobalUni(prompt);
        var buffer = Marshal.AllocHGlobal(sizeof(char) * 260);
        var list = nint.Zero;

        try
        {
            var info = new BrowseInfo
            {
                Owner = owner,
                Root = 0,
                DisplayName = buffer,
                Title = title,
                Flags = ReturnOnlyFileSystemDirectories | NewDialogStyle,
                Callback = 0,
                Parameter = 0,
                Image = 0,
            };

            list = SHBrowseForFolderW(ref info);

            if (list == nint.Zero)
            {
                return null;
            }

            return SHGetPathFromIDListW(list, buffer)
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            if (list != nint.Zero)
            {
                CoTaskMemFree(list);
            }

            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(title);
        }
    }

    /// <summary>
    /// Opens Explorer at a directory.
    /// </summary>
    /// <param name="directory">The directory to show.</param>
    /// <returns>Whether the shell accepted the request.</returns>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the one launch in the tree that does not set
    /// <c>CreateNoWindow</c>, and it cannot: it starts no process at all.</b>
    /// <c>ShellExecuteW</c> hands the verb to the shell, which opens a window in
    /// <b>Explorer's</b> process or in a new one of its own — and a window is
    /// exactly what was asked for. The house scan that requires
    /// <c>CreateNoWindow</c> reads <c>ProcessStartInfo</c> construction sites,
    /// so it does not see this and is not being evaded: there is no
    /// <c>ProcessStartInfo</c> here, and <c>CreateNoWindow</c> is not a
    /// parameter this API has.
    /// </para>
    /// <para>
    /// <b><c>explore</c> rather than <c>open</c></b>, because the verb decides
    /// whether the navigation pane is there, and a person who clicked
    /// <i>install location</i> is going to look around.
    /// </para>
    /// </remarks>
    public static bool OpenInExplorer(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        // Anything above 32 is success, which is this API's own convention and
        // is the reason the return value is not a BOOL.
        return ShellExecuteW(0, "explore", directory, null, null, ShowNormal) > 32;
    }

    /// <summary>
    /// Opens a URL with whatever is registered for it.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <returns>Whether the shell accepted the request.</returns>
    public static bool OpenUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return ShellExecuteW(0, "open", url, null, null, ShowNormal) > 32;
    }

    /// <summary>SW_SHOWNORMAL.</summary>
    private const int ShowNormal = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        public nint Owner;
        public nint Root;
        public nint DisplayName;
        public nint Title;
        public uint Flags;
        public nint Callback;
        public nint Parameter;
        public int Image;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHBrowseForFolderW", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SHBrowseForFolderW(ref BrowseInfo info);

    [LibraryImport("shell32.dll", EntryPoint = "SHGetPathFromIDListW", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHGetPathFromIDListW(nint list, nint path);

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint ShellExecuteW(nint owner, string? verb, string file, string? parameters, string? directory, int show);

    [LibraryImport("ole32.dll", EntryPoint = "CoTaskMemFree", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CoTaskMemFree(nint block);
}

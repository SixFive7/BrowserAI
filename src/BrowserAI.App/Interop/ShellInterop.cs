// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.App.Interop;

/// <summary>What asking for a folder ended in.</summary>
internal enum FolderPickOutcome
{
    /// <summary>A folder was chosen and it has a path.</summary>
    Picked,

    /// <summary>The person closed the picker without choosing.</summary>
    Cancelled,

    /// <summary>A folder was chosen and something went wrong turning it into a path.</summary>
    Failed,
}

/// <summary>
/// What <see cref="ShellInterop.PickFolder"/> answered.
/// </summary>
/// <remarks>
/// ⚠️ <b>Three states instead of a nullable string, since 2026-09-16.</b> The
/// picker used to answer <see langword="null"/> for <i>cancelled</i> and
/// <see langword="null"/> for <i>a folder was chosen and Windows would not give
/// a path for it</i>, and the caller read both as a cancel -- so the second one
/// closed the picker, changed nothing, and said nothing. The two are different
/// things to a person and are two values now.
/// </remarks>
/// <param name="Outcome">Which of the three happened.</param>
/// <param name="Path">The directory, when one was picked.</param>
/// <param name="Reason">What went wrong, when something did.</param>
internal readonly record struct FolderPick(FolderPickOutcome Outcome, string? Path, string? Reason)
{
    /// <summary>Nothing was chosen.</summary>
    public static FolderPick Cancelled { get; } = new(FolderPickOutcome.Cancelled, null, null);

    /// <summary>A folder was chosen.</summary>
    /// <param name="path">Its path.</param>
    /// <returns>The outcome.</returns>
    public static FolderPick Of(string path) => new(FolderPickOutcome.Picked, path, null);

    /// <summary>A folder was chosen and could not be turned into a path.</summary>
    /// <param name="reason">A sentence the dialog can show.</param>
    /// <returns>The outcome.</returns>
    public static FolderPick Broke(string reason) => new(FolderPickOutcome.Failed, null, reason);
}

/// <summary>
/// The shell calls: pick a folder, and open one.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b><c>SHBrowseForFolderW</c> instead of <c>IFileOpenDialog</c>, and this
/// is a decision, not an oversight.</b> The modern picker is COM, and
/// COM under NativeAOT means source-generated interop --
/// <c>[GeneratedComInterface]</c> over <c>IModalWindow</c>,
/// <c>IFileDialog</c> and <c>IFileOpenDialog</c>, which is twenty-six vtable
/// slots that have to be declared in exact order and whose only failure mode is
/// calling the wrong function at run time with no diagnostic. That is a real
/// risk to take for a folder picker, and it is a risk this repository cannot
/// retire the way it retires others: <b>no test here can open a modal window</b>,
/// so a wrong slot would be found by the maintainer clicking a button, not
/// by a run.
/// </para>
/// <para>
/// <b>What is given up is small and is stated, not glossed.</b>
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
/// with no error. That is why the entry point is <c>[STAThread]</c> -- a fact
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
    /// <c>MAX_PATH</c>, which is what <c>BROWSEINFOW.pszDisplayName</c> is
    /// documented to require and is therefore not negotiable.
    /// </summary>
    private const int DisplayNameCharacters = 260;

    /// <summary>
    /// How many characters the path buffer holds.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The Windows extended-length maximum, and not <c>MAX_PATH</c> --
    /// 2026-09-16.</b> The picker used to write the path into the same 260-char
    /// buffer it gave the shell for the display name, and
    /// <c>SHGetPathFromIDListW</c> has no length parameter at all: it assumes
    /// <c>MAX_PATH</c> and answers <c>FALSE</c> for anything longer. A person
    /// with a deep project directory therefore clicked a folder and got a silent
    /// nothing. <c>SHGetPathFromIDListEx</c> takes the length, so the buffer can
    /// be the real limit.
    /// </remarks>
    private const int PathCharacters = 32768;

    /// <summary><c>GPFIDL_DEFAULT</c>: a plain file-system path.</summary>
    private const uint PathDefault = 0x0000;

    /// <summary>
    /// Asks for a directory.
    /// </summary>
    /// <param name="owner">The window the dialog is modal to, or zero.</param>
    /// <param name="prompt">The sentence above the tree.</param>
    /// <returns>What happened.</returns>
    public static FolderPick PickFolder(nint owner, string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var title = Marshal.StringToHGlobalUni(prompt);
        var display = Marshal.AllocHGlobal(sizeof(char) * DisplayNameCharacters);
        var buffer = Marshal.AllocHGlobal(sizeof(char) * PathCharacters);
        var list = nint.Zero;

        try
        {
            var info = new BrowseInfo
            {
                Owner = owner,
                Root = 0,
                DisplayName = display,
                Title = title,
                Flags = ReturnOnlyFileSystemDirectories | NewDialogStyle,
                Callback = 0,
                Parameter = 0,
                Image = 0,
            };

            list = SHBrowseForFolderW(ref info);

            if (list == nint.Zero)
            {
                return Decide(chosen: false, resolved: false, null);
            }

            var resolved = SHGetPathFromIDListEx(list, buffer, PathCharacters, PathDefault);

            return Decide(chosen: true, resolved, resolved ? Marshal.PtrToStringUni(buffer) : null);
        }
        finally
        {
            if (list != nint.Zero)
            {
                CoTaskMemFree(list);
            }

            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(display);
            Marshal.FreeHGlobal(title);
        }
    }

    /// <summary>
    /// What the two shell calls between them mean.
    /// </summary>
    /// <remarks>
    /// <b>Separate from the P/Invokes so that it can be asserted at all.</b> No
    /// test in this repository can open a modal window, so the only part of the
    /// picker that can be held to anything is what it makes of the answers -- and
    /// that is this method, over values a test supplies.
    /// </remarks>
    /// <param name="chosen">Whether the picker returned an item list.</param>
    /// <param name="resolved">Whether that list turned into a path.</param>
    /// <param name="path">The path, when it did.</param>
    /// <returns>What happened.</returns>
    internal static FolderPick Decide(bool chosen, bool resolved, string? path)
    {
        if (!chosen)
        {
            return FolderPick.Cancelled;
        }

        return resolved && path is { Length: > 0 }
            ? FolderPick.Of(path)
            : FolderPick.Broke(
                "Windows would not give a file-system path for the folder you chose, so nothing was written."
                + " Pick a folder on a drive rather than a shell location such as This PC or a library.");
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
    /// <b>Explorer's</b> process or in a new one of its own -- and a window is
    /// exactly what was asked for. The house scan that requires
    /// <c>CreateNoWindow</c> reads <c>ProcessStartInfo</c> construction sites,
    /// so it does not see this and is not being evaded: there is no
    /// <c>ProcessStartInfo</c> here, and <c>CreateNoWindow</c> is not a
    /// parameter this API has.
    /// </para>
    /// <para>
    /// <b><c>explore</c>, not <c>open</c></b>, because the verb decides
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

    [LibraryImport("shell32.dll", EntryPoint = "SHGetPathFromIDListEx", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHGetPathFromIDListEx(nint list, nint path, int characters, uint options);

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint ShellExecuteW(nint owner, string? verb, string file, string? parameters, string? directory, int show);

    [LibraryImport("ole32.dll", EntryPoint = "CoTaskMemFree", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CoTaskMemFree(nint block);
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Interop;

/// <summary>
/// The one question BrowserAI ever asks a human, and the only channel it has to
/// ask it on.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is exactly one caller and it is the uninstall hook</b>
/// (<c>Registration.DataRootDisposal</c>), which asks whether the data root
/// should go with the program. Nothing on the serving path may reach this: a
/// dialog from a background MCP server is an invisible hang, which is the whole
/// reason <c>Runtime.BrowserConfiguration</c> refuses the browser features that
/// prompt.
/// </para>
/// <para>
/// <b>Why a message box at all.</b> The hook has no console -- Velopack starts it
/// with <c>CREATE_NO_WINDOW</c>, measured in its own launch flags -- and
/// <c>System.Console</c> is banned process-wide, so there is no text channel to
/// ask on. A window is what is left, and it is also the right shape: the person
/// who clicked <i>Uninstall</i> is looking at the screen.
/// </para>
/// <para>
/// <b>It is never reached when nobody is there.</b> Silence is established
/// before this is called, from the parent's own command line -- see
/// <c>Registration.DataRootDisposal.IsSilent</c>. A dialog inside a
/// <c>--silent</c> uninstall would be a 60-second stall ending in the hook being
/// killed.
/// </para>
/// <para>
/// <b>The default answer is the safe one, twice over.</b> The dialog opens with
/// the second button -- <i>No</i> -- focused, and a call that fails for any reason
/// answers <see langword="false"/>. Every path that cannot ask keeps the data.
/// </para>
/// </remarks>
internal static partial class UserPrompt
{
    private const uint YesNo = 0x00000004;
    private const uint IconQuestion = 0x00000020;

    /// <summary>The second button is the default one, so Enter keeps.</summary>
    private const uint DefaultButtonTwo = 0x00000100;

    /// <summary>
    /// Task-modal rather than application-modal: this process has no window of
    /// its own to be modal to.
    /// </summary>
    private const uint TaskModal = 0x00002000;

    private const uint SetForeground = 0x00010000;
    private const uint TopMost = 0x00040000;
    private const int IdYes = 6;

    /// <summary>Asks a yes/no question, with <i>no</i> as the default.</summary>
    /// <param name="title">The window title.</param>
    /// <param name="message">The question, already composed.</param>
    /// <returns>
    /// Whether the answer was <i>yes</i>. Anything else -- <i>no</i>, a closed
    /// window, a failed call -- is <see langword="false"/>.
    /// </returns>
    public static bool AskYesNo(string title, string message) =>
        MessageBoxW(
            IntPtr.Zero,
            message,
            title,
            YesNo | IconQuestion | DefaultButtonTwo | TaskModal | SetForeground | TopMost) == IdYes;

    // System32 only, on every P/Invoke in this repository (CA5392). user32 is a
    // KnownDLL, so the attribute cannot change this one's outcome; the rule is
    // every declaration, and the audit that plants a fake user32.dll beside the
    // binary and sees nothing happen is the trap rather than the rule.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}

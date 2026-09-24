// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Interop;

/// <summary>
/// Tells every top-level window that the user's environment changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>What Explorer listens for.</b> <c>WM_SETTINGCHANGE</c> with the string
/// <c>Environment</c> is the message on which Explorer reads the environment out of
/// the registry again, so a program started from Explorer afterwards is given the
/// new PATH; a program already running keeps the one it started with. Added
/// 2026-09-24 with Q294 b, whose install hook puts BrowserAI's folder on the user's
/// PATH.
/// </para>
/// <para>
/// ⚠️ <b>Bounded, and a hung window is skipped.</b> A broadcast is delivered to each
/// top-level window in turn, and every hook that sends it runs under Velopack's own
/// timeout -- fifteen seconds for the update hook. <c>SMTO_ABORTIFHUNG</c> skips a
/// window the system already knows is hung, and one second is the most any other
/// window may take. Nothing waits for an answer, because there is none to read.
/// </para>
/// </remarks>
internal static partial class EnvironmentBroadcast
{
    private const nint BroadcastWindow = 0xFFFF;
    private const uint SettingChange = 0x001A;
    private const uint AbortIfHung = 0x0002;

    /// <summary>The most one window may take to handle the message, in milliseconds.</summary>
    private const uint PerWindowBound = 1000;

    /// <summary>Broadcasts the change.</summary>
    /// <returns>Whether the broadcast was sent.</returns>
    public static bool Announce() =>
        SendMessageTimeoutW(BroadcastWindow, SettingChange, 0, "Environment", AbortIfHung, PerWindowBound, out _) != 0;

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeoutW(
        nint hWnd,
        uint msg,
        nuint wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out nuint lpdwResult);
}

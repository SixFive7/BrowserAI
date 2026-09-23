// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Interop;

/// <summary>
/// What is on the other end of this process's standard input: a pipe somebody
/// writes JSON-RPC into, or a console nobody ever closes.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists for one decision and it is a teardown decision.</b> BrowserAI
/// has two ways of learning that the conversation is over -- stdin reaching
/// end-of-file, and an <c>OpenProcess</c> handle on the launcher being signalled
/// (<see cref="ClientLivenessWatcher"/>). A console stdin disables the first:
/// there is no writer to close it, so the read parks for ever. That is fine
/// while the second still works, and it is the exact shape that produced a
/// server running until the machine was rebooted when it did not -- measured
/// 2026-09-14 against the installer's own post-install start.
/// </para>
/// <para>
/// <b><c>GetConsoleMode</c>, not <c>GetFileType</c>, and the difference
/// is <c>NUL</c>.</b> Both a console and the null device answer
/// <c>FILE_TYPE_CHAR</c>, and they behave oppositely for the only question being
/// asked here: <c>NUL</c> reports end-of-file on the first read. Only a console
/// input handle has a console mode, so this is the predicate the sentence above
/// actually means.
/// </para>
/// <para>
/// <b>Never <c>System.Console</c>.</b> It is banned process-wide -- see
/// <c>src/BrowserAI/BannedSymbols.txt</c> -- and
/// <c>Console.IsInputRedirected</c> would answer a near-enough question by
/// touching the type that owns the wire.
/// </para>
/// </remarks>
internal static partial class StandardInput
{
    private const int StdInputHandle = -10;

    private static readonly IntPtr InvalidHandleValue = new(-1);

    /// <summary>
    /// Whether standard input is a console, which is the same question as
    /// <i>will this ever report end-of-file on its own?</i>
    /// </summary>
    /// <remarks>
    /// <b>Answers <see langword="false"/> whenever it cannot tell.</b> Every
    /// caller acts on <see langword="true"/> by giving up and exiting, so an
    /// unreadable handle must read as the ordinary case: a BrowserAI that exited
    /// because it could not classify its own stdin would be worse than one that
    /// waited.
    /// </remarks>
    /// <returns>Whether standard input is a console handle.</returns>
    public static bool IsAConsole()
    {
        var handle = GetStdHandle(StdInputHandle);

        return handle != IntPtr.Zero
            && handle != InvalidHandleValue
            && GetConsoleMode(handle, out _);
    }

    // System32 only, on every P/Invoke in this repository (CA5392). kernel32 is
    // a KnownDLL and resolves before any path search, so the attribute cannot
    // change this one's outcome; it is here because the rule is every
    // declaration and the next library added may not be a KnownDLL.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int nStdHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
}

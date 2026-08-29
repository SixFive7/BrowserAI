// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Runtime.InteropServices;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// One <c>CreateWindowExW</c>, taken on this desktop at the instant a launched
/// browser was found dead, so that a death nothing else can explain can say
/// whether the desktop heap was spent.
/// </summary>
/// <remarks>
/// <para>
/// <b>For failure messages, and only for them</b> — the same bar
/// <see cref="MachineLoad"/> is held to, for the same reason. What this returns
/// is a property of whatever else is on the machine's desktop, so an assertion
/// on the verdict would be a test that passes or fails depending on the
/// developer's other windows. What may be asserted is that the reading was
/// <i>taken</i>, which is what
/// <c>StraySweepTests.ABrowserGoneBeforeItsWindowAppearedIsAskedWhetherTheDesktopHeapWasSpent</c>
/// holds.
/// </para>
/// <para>
/// <b>Why this exists at all.</b> A desktop heap spent to the byte kills a
/// Chromium before it creates a single window: <c>CreateWindowExW</c> is
/// refused, and <c>WindowImpl::Init</c> in <c>ui/gfx/win/window_impl.cc</c> ends
/// that path in a <c>NOTREACHED()</c> and a check rather than an error return —
/// and a check does not log. What comes out is a browser that died with nothing
/// on either stream, a five-line log and a clean bill of health from every
/// system-wide counter, because the resource that ran out is the one nothing can
/// be asked about: <c>UOI_HEAPSIZE</c> reports a desktop heap's <b>size</b> and
/// no documented API reports its <b>usage</b>. Reproduced deliberately over
/// eighty launches on 2026-08-27; the whole of it is in
/// [question 8](../../../QUESTIONS.md) and in
/// [kb: desktop heap](../../../kb/windows/processes.md).
/// </para>
/// <para>
/// <b>Trying the allocation is the only reading there is</b>, and this is the
/// only moment it can be taken: a heap that was full when the browser died is
/// commonly not full a second later, because the windows that filled it belong
/// to processes that come and go.
/// </para>
/// <para>
/// <b>The title is 2,048 characters, and that is the measured regime rather than
/// a round number.</b> Window text lives in the desktop heap, so the length of
/// the title <i>is</i> the size of the allocation being attempted — and the rig
/// established that a heap with one window of headroom (≈4.4 KB) still kills a
/// browser, while a one-character window fits in far less than that. A probe
/// that asked for the smallest possible allocation would report a clean create
/// on a desktop that is already killing browsers.
/// </para>
/// <para>
/// ⚠️ <b>A refusal for want of desktop heap does not reliably set a last error,
/// and this is the trap the wording below exists for.</b> Measured 2026-08-27:
/// filling with 2,048-character titles, the refusing <c>CreateWindowExW</c>
/// reported <c>GetLastError</c> = <b>0</b> on both a 512 KB heap and a
/// 20,480 KB one, while filling with one-character titles reported
/// <c>ERROR_NOT_ENOUGH_MEMORY</c>, and a desktop out of USER handles reported
/// <c>ERROR_NO_MORE_USER_HANDLES</c>. The long-title regime is the one that
/// reproduces the shape seen in the wild, so <b>a zero here is the signature and
/// not a gap in the reading</b>, and it gets a verdict of its own that says so
/// rather than being folded into "refused, cause unknown".
/// </para>
/// <para>
/// <b>The zero is this call's answer and not a stale one</b>, which is what
/// makes the paragraph above safe to act on: on .NET — and unlike .NET
/// Framework — the error information is cleared to 0 <i>before</i> the callee is
/// invoked whenever <c>SetLastError</c> is set, so
/// <see cref="Marshal.GetLastPInvokeError"/> afterwards is what this call set,
/// including when what it set is nothing. *Verified 2026-08-29 against
/// Microsoft's own reference for <c>SetLastError</c> and
/// <c>Marshal.GetLastWin32Error</c>; **read rather than run** — the generated
/// stub is not emitted to disk in this build, so nothing here has looked at it.*
/// </para>
/// <para>
/// <b>What it cannot see, said here rather than implied.</b> It reads the
/// desktop <i>this thread</i> is on, which is the desktop a browser launched by
/// this suite inherits — so it is the right desktop today and would silently
/// become the wrong one if a launcher ever gave its browsers a desktop of their
/// own. And it is one sample: a heap that was refilled between the death and the
/// probe reads clean, which is a false negative it cannot distinguish from a
/// healthy machine.
/// </para>
/// </remarks>
internal static partial class DesktopHeapProbe
{
    /// <summary>The block's own heading, which is what a message is searched for.</summary>
    public const string Heading = "--- one CreateWindowExW of our own, on this desktop, at this instant ---";

    /// <summary>The window was created: this desktop's heap had room.</summary>
    public const string NotDesktopHeapVerdict = "NOT DESKTOP HEAP";

    /// <summary>Refused, and the refusal named the shortage.</summary>
    public const string ExhaustedVerdict = "DESKTOP HEAP EXHAUSTED, named by the refusal";

    /// <summary>Refused with no last error at all, which is the measured signature.</summary>
    public const string ExhaustedSilentlyVerdict = "DESKTOP HEAP EXHAUSTED, and the refusal named nothing";

    /// <summary>Refused for a reason that is not one of the measured signatures.</summary>
    public const string RefusedElsewhereVerdict = "REFUSED FOR SOMETHING ELSE";

    /// <summary>The probe itself could not run, which is a gap and says so.</summary>
    public const string NotTakenVerdict = "READING NOT TAKEN";

    /// <summary>
    /// Every verdict this probe may report, so a test can require that a message
    /// carries exactly one.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately disjoint strings.</b> None is a substring of another, so
    /// "exactly one of these appears" is a question about which reading was taken
    /// rather than about how the sentences were worded.
    /// </remarks>
    public static IReadOnlyList<string> Verdicts { get; } =
    [
        NotDesktopHeapVerdict,
        ExhaustedVerdict,
        ExhaustedSilentlyVerdict,
        RefusedElsewhereVerdict,
        NotTakenVerdict,
    ];

    private const int TitleLength = 2048;

    private const int ErrorNotEnoughMemory = 8;

    private const int ErrorNoMoreUserHandles = 1158;

    /// <summary>
    /// <c>HWND_MESSAGE</c>: the parent that makes a window message-only —
    /// invisible, never enumerated, and scoped to this window station and
    /// desktop, which is the scope being asked about.
    /// </summary>
    private static readonly nint HwndMessage = -3;

    /// <summary>Takes the reading, and says what it means.</summary>
    /// <remarks>
    /// <b><c>STATIC</c> rather than a class of our own</b>, so that this really
    /// is one call: a registration would be a second desktop-heap allocation
    /// taken before the one being measured, and it could fail first and for the
    /// same reason, which would put a second failure mode inside a diagnostic
    /// written to remove one. <c>STATIC</c> is a system global class every
    /// process already has.
    /// </remarks>
    /// <returns>The block, headed and indented to sit inside a failure message.</returns>
    public static string Describe()
    {
        string verdict;
        string reading;

        try
        {
            var window = CreateWindowExW(
                0,
                "STATIC",
                new string('W', TitleLength),
                0,
                0,
                0,
                0,
                0,
                HwndMessage,
                nint.Zero,
                nint.Zero,
                nint.Zero);

            var error = Marshal.GetLastPInvokeError();

            if (window != nint.Zero)
            {
                // Destroyed straight away: a diagnostic that leaked a window
                // would consume the resource it was written to measure.
                _ = DestroyWindow(window);

                verdict = NotDesktopHeapVerdict;
                reading =
                    $"A message-only window carrying a {TitleLength.ToString(CultureInfo.InvariantCulture)}-character title was created "
                    + "on this desktop and destroyed again. Its heap had room for the allocation an exhausted one refuses a "
                    + "starting Chromium, so whatever killed the browser, it was not this. That is information: it retires the "
                    + "one ceiling none of the figures above can see.";
            }
            else
            {
                (verdict, reading) = Refusal(error);
            }
        }
#pragma warning disable CA1031 // Anything that stops the reading being taken is part of the finding, and none of it may replace the failure being reported.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            verdict = NotTakenVerdict;
            reading =
                $"The probe itself could not run: {failure.GetType().Name}: {failure.Message}. "
                + "The desktop-heap question is therefore open on this failure rather than answered either way.";
        }

        return string.Join(
            Environment.NewLine,
            Heading,
            $"  verdict: {verdict}",
            $"  reading: {reading}");
    }

    /// <summary>Reads a refused <c>CreateWindowExW</c> against the measured signatures.</summary>
    /// <param name="error">What the call set, or did not set.</param>
    /// <returns>The verdict and the sentence that justifies it.</returns>
    private static (string Verdict, string Reading) Refusal(int error) => error switch
    {
        0 => (
            ExhaustedSilentlyVerdict,
            "CreateWindowExW was refused and set no last error at all (GetLastError = 0). READ THAT AS THE DIAGNOSIS "
            + "RATHER THAN AS A GAP: measured 2026-08-27 on the desktop-heap rig, a refusal for want of desktop heap "
            + $"does not reliably set one, and with a {TitleLength.ToString(CultureInfo.InvariantCulture)}-character title it reported 0 on both a 512 KB heap and a "
            + "20,480 KB one, where one-character titles reported ERROR_NOT_ENOUGH_MEMORY — and the long-title regime is "
            + "the one that reproduces the shape seen in the wild. The zero is this call's own answer rather than a "
            + "stale one: on .NET the error is cleared before a SetLastError import is invoked. This is also the branch "
            + "Chromium crashes on rather than reports, which is why the browser said nothing."),
        ErrorNotEnoughMemory => (
            ExhaustedVerdict,
            $"CreateWindowExW was refused with ERROR_NOT_ENOUGH_MEMORY ({ErrorNotEnoughMemory.ToString(CultureInfo.InvariantCulture)}) on this desktop. "
            + "THAT IS THE DIAGNOSIS, and this is the only moment it can be taken: a Chromium that cannot create a window "
            + "object dies before it makes one, silently, because the path has no error return."),
        ErrorNoMoreUserHandles => (
            ExhaustedVerdict,
            $"CreateWindowExW was refused with ERROR_NO_MORE_USER_HANDLES ({ErrorNoMoreUserHandles.ToString(CultureInfo.InvariantCulture)}) on this desktop. "
            + "This desktop is out of USER handles rather than out of heap bytes, which the rig produced by filling with "
            + "many small windows instead of few large ones — 23,718 against 4,637 — and it kills a browser earlier still, "
            + "with no log file at all. Same subsystem, same outcome for the browser, different number."),
        _ => (
            RefusedElsewhereVerdict,
            $"CreateWindowExW was refused with {error.ToString(CultureInfo.InvariantCulture)} on this desktop, which is none of the three "
            + $"signatures measured on 2026-08-27: exhaustion by large allocations reports 0, by small ones ERROR_NOT_ENOUGH_MEMORY ({ErrorNotEnoughMemory.ToString(CultureInfo.InvariantCulture)}), "
            + $"and a desktop out of USER handles ERROR_NO_MORE_USER_HANDLES ({ErrorNoMoreUserHandles.ToString(CultureInfo.InvariantCulture)}). "
            + "Something is wrong with this desktop and it is not the shape this probe was written for; read the number."),
    };

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
}

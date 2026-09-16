// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.App.Interop;

/// <summary>
/// The Win32 task dialog, declared once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raw <c>TaskDialogIndirect</c> rather than a framework wrapper</b>, because
/// there is no framework wrapper this product may use: WPF and WinForms both
/// have trimming disabled in the SDK and therefore do not build under
/// <c>PublishAot</c>, and <c>System.Windows.Forms.TaskDialog</c> is part of the
/// second of those.
/// </para>
/// <para>
/// ⚠️ <b><c>TASKDIALOGCONFIG</c> is <c>#pragma pack(1)</c> in the Windows
/// headers</b>, which is the single likeliest way to get this wrong: the
/// natural C# layout inserts padding before every pointer and the whole
/// structure then means something else, field by field, with no diagnostic at
/// all — the call simply returns <c>E_INVALIDARG</c>, or worse, succeeds and
/// renders nonsense. <see cref="TaskDialogConfig"/> declares
/// <see cref="LayoutKind.Sequential"/> with <c>Pack = 1</c>, and its size is
/// asserted against the documented 160 bytes by the suite rather than trusted.
/// </para>
/// <para>
/// ⚠️ <b>The callback is <c>[UnmanagedCallersOnly]</c> and every parameter is
/// blittable.</b> A <see cref="bool"/> anywhere in that signature is a runtime
/// failure rather than a compile error, because the attribute forbids
/// marshalling and <see cref="bool"/> is the type that most looks as though it
/// would not need any. Windows passes <c>BOOL</c>, which is a four-byte
/// <see cref="int"/>.
/// </para>
/// </remarks>
internal static partial class TaskDialogInterop
{
    /// <summary>Everything one page of the dialog is.</summary>
    /// <remarks>
    /// The two unions in the Windows declaration — icon-or-resource, for the
    /// main icon and the footer icon — are declared here as the pointer half,
    /// because that is the half this product uses: an <c>HICON</c> loaded from
    /// our own resources, with <see cref="Flags.UseHIconMain"/> set to say so.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    internal struct TaskDialogConfig
    {
        public uint Size;
        public nint Parent;
        public nint Instance;
        public uint Flags;
        public uint CommonButtons;
        public nint WindowTitle;
        public nint MainIcon;
        public nint MainInstruction;
        public nint Content;
        public uint ButtonCount;
        public nint Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public nint RadioButtons;
        public int DefaultRadioButton;
        public nint VerificationText;
        public nint ExpandedInformation;
        public nint ExpandedControlText;
        public nint CollapsedControlText;
        public nint FooterIcon;
        public nint Footer;
        public nint Callback;
        public nint CallbackData;
        public uint Width;
    }

    /// <summary>One custom button.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    internal struct TaskDialogButton
    {
        public int Id;
        public nint Text;
    }

    /// <summary>What the documented layout weighs on x64.</summary>
    /// <remarks>
    /// <b>Written down so the suite can disagree with it.</b> Four bytes at the
    /// front, twenty-three fields after it, no padding anywhere: eight
    /// four-byte fields and sixteen pointers. A natural-packing build of the
    /// same struct is 184 bytes, and the difference is the whole defect.
    /// </remarks>
    public const int ConfigSize = 160;

    /// <summary>What one button weighs.</summary>
    public const int ButtonSize = 12;

    /// <summary>The flags this product sets.</summary>
    [Flags]
    internal enum Flags : uint
    {
        /// <summary>No flag.</summary>
        None = 0,

        /// <summary>Content and footer may carry <c>&lt;a href="…"&gt;</c>.</summary>
        EnableHyperlinks = 0x0001,

        /// <summary>The main icon field is an <c>HICON</c> rather than a resource id.</summary>
        UseHIconMain = 0x0002,

        /// <summary>Escape and the close box work even with no cancel button.</summary>
        AllowDialogCancellation = 0x0008,

        /// <summary>Custom buttons render as command links.</summary>
        UseCommandLinks = 0x0010,

        /// <summary>
        /// <c>TDN_TIMER</c> is delivered roughly every 200 ms while the dialog
        /// is up.
        /// </summary>
        /// <remarks>
        /// It is how a window whose every action runs inside one callback can
        /// wait for something without freezing: the work runs off this thread
        /// and the tick asks whether it has finished.
        /// </remarks>
        CallbackTimer = 0x0800,

        /// <summary>The dialog sizes itself to its content rather than to a fixed width.</summary>
        SizeToContent = 0x0100_0000,

        /// <summary>Position relative to the parent window rather than the screen.</summary>
        PositionRelativeToWindow = 0x1000,
    }

    /// <summary>The stock buttons.</summary>
    [Flags]
    internal enum CommonButton : uint
    {
        /// <summary>None.</summary>
        None = 0,

        /// <summary>OK.</summary>
        Ok = 0x0001,

        /// <summary>Close.</summary>
        Close = 0x0020,
    }

    /// <summary>The stock button ids, which are the <c>IDOK</c> family.</summary>
    internal static class ButtonId
    {
        /// <summary>IDOK.</summary>
        public const int Ok = 1;

        /// <summary>IDCANCEL — what Escape and the close box report.</summary>
        public const int Cancel = 2;

        /// <summary>IDCLOSE.</summary>
        public const int Close = 8;
    }

    /// <summary>The notifications this product handles.</summary>
    internal static class Notification
    {
        /// <summary>The dialog exists and its controls are created.</summary>
        public const uint Created = 0;

        /// <summary>A button was clicked. Returning <c>S_FALSE</c> keeps the dialog open.</summary>
        public const uint ButtonClicked = 2;

        /// <summary>A hyperlink was clicked. <c>lParam</c> is its href.</summary>
        public const uint HyperlinkClicked = 3;

        /// <summary>
        /// The callback timer ticked. <c>wParam</c> is the milliseconds since
        /// the dialog was created, or since the last tick that answered
        /// <c>S_FALSE</c>.
        /// </summary>
        /// <remarks>
        /// Only delivered with <see cref="Flags.CallbackTimer"/> set.
        /// </remarks>
        public const uint Timer = 4;
    }

    /// <summary>The messages this product sends back into an open dialog.</summary>
    internal static class Message
    {
        private const uint User = 0x0400;

        /// <summary>Replace the whole page, buttons included.</summary>
        public const uint NavigatePage = User + 101;

        /// <summary>Set an element's text and resize the dialog around it.</summary>
        public const uint SetElementText = User + 108;

        /// <summary>Set an element's text without resizing.</summary>
        public const uint UpdateElementText = User + 114;
    }

    /// <summary>Which piece of text a message is about.</summary>
    internal static class Element
    {
        /// <summary>The body text.</summary>
        public const nint Content = 0;

        /// <summary>The footer.</summary>
        public const nint Footer = 2;

        /// <summary>The large heading.</summary>
        public const nint MainInstruction = 3;
    }

    /// <summary><c>S_OK</c>: the notification was handled and the default follows.</summary>
    public const int Ok = 0;

    /// <summary><c>S_FALSE</c>: from a button click, do not close the dialog.</summary>
    public const int False = 1;

    /// <summary>
    /// The resource id the .NET SDK writes an <c>ApplicationIcon</c> group under.
    /// </summary>
    /// <remarks>
    /// <b>32512 is <c>IDI_APPLICATION</c>'s numeric value</b>, and it is what
    /// the SDK's Win32 resource writer uses for the icon group it generates from
    /// the <c>ApplicationIcon</c> property. A build that ever stopped doing so
    /// would leave <see cref="LoadIconW"/> returning null here, which is why the
    /// caller falls back to the system icon rather than showing nothing.
    /// </remarks>
    public const int ApplicationIconResource = 32512;

    /// <summary>Shows one task dialog and blocks until it closes.</summary>
    /// <param name="config">The page.</param>
    /// <param name="button">Which button closed it.</param>
    /// <param name="radioButton">Unused; required by the entry point.</param>
    /// <param name="verificationChecked">Unused; required by the entry point.</param>
    /// <returns>An <c>HRESULT</c>.</returns>
    [LibraryImport("comctl32.dll", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int TaskDialogIndirect(
        ref TaskDialogConfig config,
        out int button,
        out int radioButton,
        out int verificationChecked);

    /// <summary>Sends a message to an open dialog.</summary>
    /// <param name="window">The dialog.</param>
    /// <param name="message">One of <see cref="Message"/>.</param>
    /// <param name="wParam">Message specific.</param>
    /// <param name="lParam">Message specific.</param>
    /// <returns>Message specific.</returns>
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    /// <summary>This process's own module handle.</summary>
    /// <param name="moduleName">Null for this executable.</param>
    /// <returns>The handle.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetModuleHandleW(nint moduleName);

    /// <summary><c>SM_CXICON</c>: the width of the large icon at a given DPI.</summary>
    public const int IconWidthMetric = 11;

    /// <summary><c>SM_CYICON</c>: its height.</summary>
    public const int IconHeightMetric = 12;

    /// <summary>Loads an icon by numeric resource id.</summary>
    /// <param name="instance">The module, or zero for the system icons.</param>
    /// <param name="resource">The id, cast to a pointer.</param>
    /// <returns>The icon, or zero.</returns>
    /// <remarks>
    /// ⚠️ <b>It answers the 32×32 image and nothing else.</b> There is no size and
    /// no DPI in this call: it takes whichever image the group holds at the
    /// system's <i>classic</i> icon size, which a Per-Monitor-V2 process then
    /// draws stretched. <see cref="LoadImageW"/> is the one that takes a size, and this is
    /// kept as its fallback.
    /// </remarks>
    [LibraryImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint LoadIconW(nint instance, nint resource);

    /// <summary><c>IMAGE_ICON</c>.</summary>
    public const uint ImageIcon = 1;

    /// <summary><c>LR_DEFAULTCOLOR</c>: no flags at all.</summary>
    public const uint LoadDefaultColor = 0x0000;

    /// <summary>
    /// Loads the image from an icon group that is closest to a given size,
    /// scaling it if it has to.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><c>LoadImageW</c> rather than <c>LoadIconWithScaleSize</c>, and
    /// the reason is not taste — measured 2026-09-16.</b> The comctl32 function
    /// is the one the documentation points at for this job and it is
    /// <b>exported by ORDINAL only</b>: a <c>LibraryImport</c> naming it fails at
    /// the call with <c>EntryPointNotFoundException: Unable to find an entry
    /// point named 'LoadIconWithScaleSize' in native library 'comctl32.dll'</c>,
    /// which reaches the process boundary from inside <c>Show()</c> and takes the
    /// window with it. Importing by ordinal would work and would be a number with
    /// nothing behind it. <c>LoadImageW</c> is exported by name from
    /// <c>user32</c>, takes the same two dimensions, and picks the closest image
    /// from the group.
    /// </remarks>
    /// <param name="instance">The module.</param>
    /// <param name="name">The resource id, cast to a pointer.</param>
    /// <param name="type">One of the <c>IMAGE_</c> values.</param>
    /// <param name="width">The width wanted, in physical pixels.</param>
    /// <param name="height">The height wanted.</param>
    /// <param name="load">The <c>LR_</c> flags.</param>
    /// <returns>The image, or zero.</returns>
    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint LoadImageW(nint instance, nint name, uint type, int width, int height, uint load);

    /// <summary>Releases an icon this process loaded and owns.</summary>
    /// <param name="icon">The icon.</param>
    /// <returns>Whether it went.</returns>
    [LibraryImport("user32.dll", EntryPoint = "DestroyIcon", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint icon);

    /// <summary>The DPI a window is being drawn at.</summary>
    /// <param name="window">The window.</param>
    /// <returns>Its DPI, or zero when the handle is not one.</returns>
    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetDpiForWindow(nint window);

    /// <summary>The system DPI, for the moment before there is a window.</summary>
    /// <returns>The DPI.</returns>
    [LibraryImport("user32.dll", EntryPoint = "GetDpiForSystem", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetDpiForSystem();

    /// <summary>A system metric, at a DPI rather than at the process's own.</summary>
    /// <param name="metric">One of the <c>SM_</c> values.</param>
    /// <param name="dpi">The DPI to answer for.</param>
    /// <returns>The metric.</returns>
    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetricsForDpi", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetSystemMetricsForDpi(int metric, uint dpi);
}

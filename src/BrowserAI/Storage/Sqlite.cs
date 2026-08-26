// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Storage;

/// <summary>
/// The SQLite compiled from the amalgamation in <c>third-party/sqlite</c> and
/// linked into this executable.
/// </summary>
/// <remarks>
/// <para>
/// <b>One declaration, deliberately.</b> The record this product keeps is not
/// stored in SQLite yet; what is here is the half that has to be proved before
/// the rest is designed against it — that a vendored amalgamation compiles, that
/// ILC links it, and that the binary can say which version it got. Everything
/// else the store needs — open, prepare, step, bind, column, finalize, close —
/// arrives with the storage layer, in this file.
/// </para>
/// <para>
/// <b>Why this is not in <c>Interop\</c>.</b> Every rule that directory carries
/// is a rule about Win32: a struct checked against Microsoft's own Win32
/// metadata, <c>Marshal.GetLastPInvokeError</c> read before anything else runs,
/// a System32-only search path, and the never-by-image-name ban. None of them
/// applies to a statically linked third-party C library, and putting this there
/// would mean writing an exception into each of them.
/// </para>
/// <para>
/// <b>The module name is <c>e_sqlite3</c> and the archive is <c>sqlite3.lib</c>,
/// and the mismatch is intended.</b> Under the published binary the name is
/// never looked up at all: <c>DirectPInvoke</c> plus <c>NativeLibrary</c> in
/// <c>build/Sqlite.targets</c> make the linker resolve
/// <c>sqlite3_libversion</c> out of the archive at publish time, so no library
/// is loaded and no search is performed. The name matters only for the other
/// case — a CoreCLR host, which is how the test suite runs product code — where
/// it is the name every package in this ecosystem gives the loose native DLL.
/// Choosing a private name would orphan that.
/// </para>
/// <para>
/// ⚠️ <b>So calling this under CoreCLR throws <c>DllNotFoundException</c> until
/// something puts <c>e_sqlite3.dll</c> beside the host</b>, and nothing does
/// today: the product references no native package, which is what keeps the
/// publish output a single file by construction. The one caller is
/// <c>Program.Main</c>, which only ever runs in the published binary.
/// </para>
/// <para>
/// <b><c>SQLITE_OMIT_AUTOINIT</c> is in the compile flags and this call is
/// unaffected by it.</b> <c>sqlite3_libversion</c> returns a pointer to a string
/// constant and touches no global state, so it is one of the few entry points
/// that needs no <c>sqlite3_initialize</c> in front of it. The storage layer
/// will need one; this does not.
/// </para>
/// </remarks>
internal static partial class Sqlite
{
    /// <summary>
    /// The module every declaration here binds, in both stacks.
    /// </summary>
    /// <remarks>
    /// A constant rather than a literal per declaration, so that the name the
    /// build's <c>DirectPInvoke</c> item switches to a direct call and the name
    /// the declarations use are one edit rather than two.
    /// </remarks>
    public const string Module = "e_sqlite3";

    /// <summary>What is reported when the library answers with no version at all.</summary>
    /// <remarks>
    /// A null here would mean <c>sqlite3_libversion</c> returned a null pointer,
    /// which cannot happen in a correctly linked build. It is named rather than
    /// coalesced to an empty string so that the impossible case reads as itself
    /// in the record instead of as an absent field.
    /// </remarks>
    public const string UnknownVersion = "<unknown>";

    /// <summary>The version of the SQLite this binary was linked against.</summary>
    /// <remarks>
    /// <b>Read from the library rather than from the source it was built from.</b>
    /// The amalgamation's own <c>SQLITE_VERSION</c> is what a reader of the tree
    /// would quote and it is one compile step away from what the binary
    /// contains — a build that linked a stale archive would report the stale
    /// version here, which is exactly the disagreement worth being able to see.
    /// </remarks>
    public static string Version => Marshal.PtrToStringUTF8(LibVersion()) ?? UnknownVersion;

    /// <summary>
    /// <c>const char *sqlite3_libversion(void)</c> — a pointer to a string
    /// constant inside the library, never freed and never owned by us.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><c>SafeDirectories</c>, and NOT the <c>System32</c> every
    /// declaration under <c>Interop\</c> carries.</b> CA5392 requires some
    /// value, and the difference between those two was measured on 2026-08-26
    /// rather than reasoned about, .NET 10.0.11 / win-x64, against a native DLL
    /// sitting beside the host: <c>System32</c> threw
    /// <c>DllNotFoundException</c>, and <c>SafeDirectories</c>, no attribute at
    /// all, and <c>AssemblyDirectory</c> all loaded it.
    /// <c>LOAD_LIBRARY_SEARCH_DEFAULT_DIRS</c> covers the application directory
    /// as well as System32; the System32-only flag does not, and it does not
    /// fall back.
    /// </para>
    /// <para>
    /// That is the correct value here for the case this attribute can affect at
    /// all. Under the published binary nothing is searched — the symbol is
    /// resolved by the linker — and under a CoreCLR host, which is the only
    /// other way this code can run, the library would be a loose
    /// <c>e_sqlite3.dll</c> beside the host and <c>System32</c> would refuse to
    /// find it. <c>System32</c> is right for the Win32 declarations because a
    /// system DLL loaded from anywhere else is the attack; it is simply wrong
    /// for a third-party library that is not an OS component, and copying the
    /// habit here would have looked identical and failed later.
    /// </para>
    /// <para>
    /// ⚠️ <b>The trap that makes this hard to see</b>, measured in the same
    /// pass: the resolved library is cached per module name, so once <i>any</i>
    /// declaration naming <c>e_sqlite3</c> has loaded it, a <c>System32</c> one
    /// beside it succeeds too. Ordering the four probes with <c>System32</c>
    /// second made all four pass. A file that mixed the values would therefore
    /// test clean and fail on whichever declaration happened to run first.
    /// </para>
    /// <para>
    /// <c>SafeDirectories</c> is also the strongest value CA5393 accepts:
    /// <c>AssemblyDirectory</c> and <c>ApplicationDirectory</c> are on its
    /// unsafe list, and both were measured to work here — so this is not a
    /// compromise, it is the one that is both accepted and correct.
    /// </para>
    /// </remarks>
    /// <returns>The pointer.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_libversion")]
    private static partial IntPtr LibVersion();
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Storage;

/// <summary>
/// The SQLite compiled from the amalgamation in <c>third-party/sqlite</c> and
/// linked into this executable: the entry points, the result codes, and the two
/// handle types that own what SQLite hands back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hand-written, and the whole managed layer above it is this repository's.</b>
/// The alternative was <c>Microsoft.Data.Sqlite.Core</c> plus
/// <c>SQLitePCLRaw</c> plus a provider — three packages, three notices, and
/// their IL in front of a publish that fails on one ILC warning. What that
/// would buy is ADO.NET's conveniences over a store that runs a dozen fixed
/// statements. What it costs is the one thing this product cannot spend.
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
/// <c>build/Sqlite.targets</c> make the linker resolve every entry point out of
/// the archive at publish time, so no library is loaded and no search is
/// performed. The name matters only for the other case — a CoreCLR host, which
/// is how the test suite runs product code — where it is the name every package
/// in this ecosystem gives the loose native DLL, and where
/// <c>SourceGear.sqlite3</c> in the test project is what puts one on disk.
/// Choosing a private name would orphan that.
/// </para>
/// <para>
/// ⚠️ <b><c>SQLITE_OMIT_AUTOINIT</c> is in the compile flags, so
/// <see cref="EnsureInitialized"/> is not optional — and what happens without
/// it is worse than a refusal.</b> Without that flag <c>sqlite3_open_v2</c>
/// calls <c>sqlite3_initialize</c> for you; with it, sqlite.org says the
/// behaviour of an entry point that needs the library initialised is
/// <i>undefined</i>. Measured 2026-08-26 against the published binary, with
/// <see cref="EnsureInitialized"/>'s body removed: the first
/// <c>sqlite3_open_v2</c> takes an <b>access violation</b> —
/// <c>0xC0000005</c>, exit code <c>-1073741819</c> — and the server dies before
/// it answers <c>initialize</c>. Not <c>SQLITE_MISUSE</c>, not any result code:
/// nothing managed sees it and no <c>catch</c> can stand in front of it.
/// </para>
/// <para>
/// <b>And the omission is invisible in the suite.</b> The loose DLL a CoreCLR
/// host loads is built <i>without</i> the flag and initialises itself, so this
/// whole class of failure appears only in the published binary — which is why
/// the startup record carries <see cref="BuildReport"/> and a test reads it
/// back off the published slice.
/// </para>
/// <para>
/// ⚠️ <b>One search-path value for the whole file, and it has to be repeated on
/// every declaration because the attribute cannot be put anywhere else.</b>
/// <c>DefaultDllImportSearchPaths</c> is valid on an assembly or a method and
/// on nothing in between — CS0592, measured here rather than assumed — so the
/// type-level declaration that would have made mixing unexpressible does not
/// compile, and assembly level would re-point <c>Interop\</c>'s Win32
/// declarations at the same value. That matters because of the trap P0
/// measured: the resolved library is cached per module name, so once
/// <i>any</i> declaration naming <c>e_sqlite3</c> has loaded it, a wrong one
/// beside it succeeds too — a file that mixed the values would test clean and
/// fail on whichever declaration happened to run first. <b>Nothing enforces the
/// uniformity below; a reader does.</b>
/// </para>
/// <para>
/// The value is <c>SafeDirectories</c> and not the <c>System32</c> every
/// declaration under <c>Interop\</c> carries: measured 2026-08-26 on .NET
/// 10.0.11, a System32-only declaration cannot find a library beside the host
/// at all, because <c>LOAD_LIBRARY_SEARCH_SYSTEM32</c> neither covers the
/// application directory nor falls back to it. <c>System32</c> is right for
/// Win32 because a system DLL loaded from anywhere else is the attack; it is
/// simply wrong for a third-party library that is not an OS component.
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

    /// <summary><c>SQLITE_OK</c>.</summary>
    public const int Ok = 0;

    /// <summary><c>SQLITE_ERROR</c> — a generic failure; <c>sqlite3_errmsg</c> says which.</summary>
    public const int GenericError = 1;

    /// <summary><c>SQLITE_BUSY</c> — another writer holds the database.</summary>
    public const int Busy = 5;

    /// <summary><c>SQLITE_CANTOPEN</c> — the file could not be opened or created.</summary>
    public const int CannotOpen = 14;

    /// <summary><c>SQLITE_MISUSE</c> — this library was used wrongly, not the database.</summary>
    /// <remarks>
    /// ⚠️ <b>It is not what an uninitialised library answers, and assuming it
    /// was is the mistake this remark exists to stop.</b> Under
    /// <c>SQLITE_OMIT_AUTOINIT</c> an open with no <c>sqlite3_initialize</c> in
    /// front of it does not return a code at all: it faults — measured, see
    /// <see cref="EnsureInitialized"/>. What this code really covers is misuse
    /// SQLite can still see, such as reusing a statement after its connection
    /// has gone.
    /// </remarks>
    public const int Misuse = 21;

    /// <summary><c>SQLITE_RANGE</c> — a bind index outside the statement's parameters.</summary>
    public const int Range = 25;

    /// <summary><c>SQLITE_NOTADB</c> — the file is not a database.</summary>
    public const int NotADatabase = 26;

    /// <summary><c>SQLITE_ROW</c> — <c>sqlite3_step</c> produced a row.</summary>
    public const int Row = 100;

    /// <summary><c>SQLITE_DONE</c> — <c>sqlite3_step</c> finished.</summary>
    public const int Done = 101;

    /// <summary><c>SQLITE_OPEN_READONLY</c>.</summary>
    public const int OpenReadOnly = 0x00000001;

    /// <summary><c>SQLITE_OPEN_READWRITE</c>.</summary>
    public const int OpenReadWrite = 0x00000002;

    /// <summary><c>SQLITE_OPEN_CREATE</c>.</summary>
    public const int OpenCreate = 0x00000004;

    /// <summary><c>SQLITE_INTEGER</c>, the type of a column value.</summary>
    public const int TypeInteger = 1;

    /// <summary><c>SQLITE_TEXT</c>, the type of a column value.</summary>
    public const int TypeText = 3;

    /// <summary><c>SQLITE_BLOB</c>, the type of a column value.</summary>
    public const int TypeBlob = 4;

    /// <summary><c>SQLITE_NULL</c>, the type of a column value.</summary>
    public const int TypeNull = 5;

    /// <summary>
    /// <c>SQLITE_TRANSIENT</c> — tell SQLite to copy the bound bytes before the
    /// bind call returns.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Never <c>SQLITE_STATIC</c> from managed code.</b> Every buffer this
    /// layer binds is a managed array pinned for the duration of the call and
    /// free to move the instant it returns; telling SQLite the bytes are static
    /// hands it a pointer the garbage collector is entitled to invalidate,
    /// which is a use-after-move that reads as data corruption rather than as a
    /// crash.
    /// </remarks>
    public static IntPtr Transient { get; } = new(-1);

    /// <summary>
    /// The compile-time options <c>build/Sqlite.targets</c> passes, spelled the
    /// way <c>PRAGMA compile_options</c> reports them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This list and the build's <c>/D</c> switches are two spellings of one
    /// decision, and a test holds them together in both directions</b>
    /// (<c>SqliteTests.TheIntendedCompileOptionsAreTheOnesTheBuildPasses</c>).
    /// They cannot simply be derived from one another: SQLite's own
    /// <c>ctime.c</c> reports some options with their value and some without,
    /// so <c>SQLITE_STRICT_SUBTYPE=1</c> arrives here as a bare
    /// <c>STRICT_SUBTYPE</c> while <c>SQLITE_DQS=0</c> arrives as
    /// <c>DQS=0</c>. The values are the half worth keeping, because a flag
    /// whose value drifted — <c>DQS=3</c>, the default, instead of
    /// <c>DQS=0</c> — is present under any check that compares names alone.
    /// </para>
    /// <para>
    /// <b><c>THREADSAFE</c> is deliberately absent from this list and is checked
    /// separately</b> by <see cref="RequireASupportedBuild"/>. It is the one
    /// deviation from sqlite.org's recommended set: the recommendation includes
    /// <c>SQLITE_THREADSAFE=0</c>, which is correct only where every call is
    /// provably on one thread, and this process serves an async message loop
    /// with a background sweep, a background update check and an idle timer in
    /// it. What that makes it is not an <i>intended flag</i> but a floor under
    /// every build this code may run against, the loose DLL a test host loads
    /// included — so it is asserted where it holds rather than listed where it
    /// would not.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> IntendedCompileOptions { get; } =
    [
        "DEFAULT_MEMSTATUS=0",
        "DEFAULT_WAL_SYNCHRONOUS=1",
        "DQS=0",
        "LIKE_DOESNT_MATCH_BLOBS",
        "MAX_EXPR_DEPTH=0",
        "OMIT_AUTOINIT",
        "OMIT_DECLTYPE",
        "OMIT_DEPRECATED",
        "OMIT_PROGRESS_CALLBACK",
        "OMIT_SHARED_CACHE",
        "STRICT_SUBTYPE",
        "USE_ALLOCA",
    ];

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
    /// Every option <c>PRAGMA compile_options</c> reports for the library this
    /// process is actually bound to.
    /// </summary>
    /// <remarks>
    /// Read once and cached: it cannot change while the process lives, and the
    /// read costs an in-memory database.
    /// </remarks>
    public static IReadOnlyList<string> CompileOptions => LazyCompileOptions.Value;

    /// <summary>
    /// The intended options this library does <b>not</b> report, in the order
    /// <see cref="IntendedCompileOptions"/> lists them.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Expected to be non-empty under a CoreCLR host and empty in the
    /// published binary, and that asymmetry is the point.</b> The loose
    /// <c>e_sqlite3.dll</c> a test host loads is somebody else's build with
    /// somebody else's flags; the archive ILC links is this repository's, built
    /// by <c>build/Sqlite.targets</c> from vendored source. So this is a claim
    /// about <i>the artifact</i> and can only be enforced against one, which is
    /// why the product reports it and a test asserts it rather than the other
    /// way round.
    /// </remarks>
    public static IReadOnlyList<string> MissingIntendedCompileOptions =>
        [.. IntendedCompileOptions.Where(option => !CompileOptions.Contains(option, StringComparer.Ordinal))];

    /// <summary>
    /// One field for the startup record: whether the linked library is the
    /// build this tree intends, and what is missing when it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It reports and never throws, and that is deliberate for exactly one
    /// phase.</b> The record is not stored in SQLite yet, so a startup that
    /// refused to proceed on a storage question would be refusing over
    /// something nothing has asked for. What makes the report load-bearing
    /// anyway is that a test reads it back off the published binary's own
    /// process log, so an archive that was never compiled, or compiled with
    /// different flags, is a red build rather than a discovery at the moment a
    /// session first writes.
    /// </para>
    /// <para>
    /// <b>The unavailable arm is a real outcome and says so.</b> A host with no
    /// <c>e_sqlite3</c> to load reaches this line, and <i>"the library is not
    /// here"</i> is a different fact from <i>"the library is here and is the
    /// wrong build"</i>.
    /// </para>
    /// <para>
    /// ⚠️ <b>The <c>catch</c> is not a safety net for the failure that matters
    /// most.</b> A library that was never initialised does not raise anything
    /// this can catch — it faults, and the process is gone
    /// (<see cref="EnsureInitialized"/>, measured). So this method is
    /// defensive about the cases that <i>are</i> exceptions, and the one it
    /// cannot be defensive about is handled by calling
    /// <see cref="EnsureInitialized"/> rather than by catching afterwards.
    /// </para>
    /// </remarks>
    public static string BuildReport
    {
        get
        {
            try
            {
                RequireASupportedBuild();

                var missing = MissingIntendedCompileOptions;

                return missing.Count is 0
                    ? "intended"
                    : "missing " + string.Join(',', missing);
            }
            catch (Exception failure) when (failure is SqliteException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return $"<unavailable: {failure.Message}>";
            }
        }
    }

    /// <summary>
    /// Calls <c>sqlite3_initialize</c> once, before anything else in this
    /// library is used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Required, because <c>SQLITE_OMIT_AUTOINIT</c> is in the compile
    /// flags — and the cost of forgetting is a dead process rather than an
    /// error.</b> Without that flag every entry point that needs the library
    /// initialised calls this itself; with it, sqlite.org calls the behaviour
    /// undefined, and what this build actually does was measured 2026-08-26
    /// rather than assumed: the published binary, with this method's body
    /// removed, took an <b>access violation</b> inside the first
    /// <c>sqlite3_open_v2</c> — <c>0xC0000005</c>, exit code
    /// <c>-1073741819</c> — and closed its stdout without answering
    /// <c>initialize</c>. No result code, no exception, nothing a
    /// <c>catch</c> can be put in front of.
    /// </para>
    /// <para>
    /// <b>Idempotent by SQLite's own contract</b>, which is what lets every
    /// open call it rather than one arranged caller: <c>sqlite3_initialize</c>
    /// is a harmless no-op after the first success, and the flag is not what
    /// makes it so.
    /// </para>
    /// </remarks>
    /// <exception cref="SqliteException">The library could not be initialised.</exception>
    public static void EnsureInitialized()
    {
        lock (InitialisationGate)
        {
            if (_initialised)
            {
                return;
            }

            var result = Initialize();

            if (result is not Ok)
            {
                throw new SqliteException(
                    result,
                    $"sqlite3_initialize failed with result {result}. Nothing in this library can be used until it succeeds.");
            }

            _initialised = true;
        }
    }

    /// <summary>
    /// Refuses a library whose thread safety was compiled out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one compile-time property asserted at run time rather than
    /// reported</b>, because it is the only one whose absence is a correctness
    /// fault under every host rather than a claim about which archive was
    /// linked. <c>SQLITE_THREADSAFE=0</c> removes the mutexes; this process
    /// reaches storage from an async message loop, a background sweep, a
    /// background update check and an idle timer, so a library without them
    /// corrupts quietly rather than failing.
    /// </para>
    /// <para>
    /// It is also the deviation this repository takes from sqlite.org's own
    /// recommended set, so a build that silently took the recommendation whole
    /// is precisely what this catches.
    /// </para>
    /// </remarks>
    /// <exception cref="SqliteException">The library reports <c>THREADSAFE=0</c>.</exception>
    public static void RequireASupportedBuild()
    {
        EnsureInitialized();

        // Serialized (1) and multi-thread (2) both keep the mutexes this
        // process needs; only 0 removes them. Anything the library does not
        // report at all is a build too old or too odd to reason about, and is
        // refused for saying nothing rather than for saying no.
        if (CompileOptions.Contains("THREADSAFE=1", StringComparer.Ordinal)
            || CompileOptions.Contains("THREADSAFE=2", StringComparer.Ordinal))
        {
            return;
        }

        throw new SqliteException(
            Misuse,
            "The linked SQLite does not report THREADSAFE=1 or THREADSAFE=2, so its mutexes may have been compiled out. "
            + "BrowserAI reaches storage from an async message loop and three background timers, and a library without "
            + "mutexes corrupts quietly instead of failing. It reports: "
            + (CompileOptions.Count is 0 ? "nothing at all." : string.Join(", ", CompileOptions)));
    }

    /// <summary>The text SQLite gives for a result code with no database behind it.</summary>
    /// <remarks>
    /// <c>sqlite3_errmsg</c> is the better message and needs a handle;
    /// <c>sqlite3_errstr</c> is the fallback for the case where the handle is
    /// what failed to exist. Both are used, and which one applies is decided at
    /// the call site rather than here.
    /// </remarks>
    /// <param name="result">The result code.</param>
    /// <returns>The description, or the number when the library has none.</returns>
    public static string Describe(int result) =>
        Marshal.PtrToStringUTF8(ErrorString(result)) ?? $"result {result}";

    /// <summary>The lazily read, permanently cached compile options.</summary>
    /// <remarks>
    /// A <see cref="Lazy{T}"/> rather than the double-checked field
    /// <see cref="EnsureInitialized"/> uses, because this one has a value and a
    /// value published without a barrier is the defect this type exists not to
    /// have. It opens an in-memory database, which needs
    /// <see cref="EnsureInitialized"/> and never re-enters this property.
    /// </remarks>
    private static Lazy<IReadOnlyList<string>> LazyCompileOptions { get; } = new(ReadCompileOptions);

    /// <summary>Serialises the one-time <c>sqlite3_initialize</c>.</summary>
    private static readonly Lock InitialisationGate = new();

    /// <summary>Whether <c>sqlite3_initialize</c> has already succeeded.</summary>
    private static bool _initialised;

    /// <summary>Asks an in-memory database what the library was compiled with.</summary>
    /// <returns>Every reported option, in the order SQLite lists them.</returns>
    private static IReadOnlyList<string> ReadCompileOptions()
    {
        // In memory, so the answer costs no file and can be asked before the
        // product knows where its data lives. `PRAGMA compile_options` is a
        // property of the library rather than of the database it is asked
        // through, so the two are equivalent and only one of them needs a path.
        using var database = SqliteDatabase.OpenInMemory();

        return database.Query("PRAGMA compile_options;");
    }

    /// <summary>
    /// <c>int sqlite3_initialize(void)</c> — initialises the library.
    /// </summary>
    /// <returns><see cref="Ok"/>, or the failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_initialize")]
    private static partial int Initialize();

    /// <summary>
    /// <c>const char *sqlite3_libversion(void)</c> — a pointer to a string
    /// constant inside the library, never freed and never owned by us.
    /// </summary>
    /// <remarks>
    /// One of the few entry points unaffected by <c>SQLITE_OMIT_AUTOINIT</c>:
    /// it returns a string constant and touches no global state, so it needs no
    /// <c>sqlite3_initialize</c> in front of it. That is why the startup record
    /// could carry the version before this layer existed.
    /// </remarks>
    /// <returns>The pointer.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_libversion")]
    private static partial IntPtr LibVersion();

    /// <summary>
    /// <c>const char *sqlite3_errstr(int)</c> — the generic text for a result
    /// code, with no database involved.
    /// </summary>
    /// <param name="result">The result code.</param>
    /// <returns>A pointer to a string constant inside the library.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_errstr")]
    private static partial IntPtr ErrorString(int result);

    /// <summary>
    /// <c>int sqlite3_open_v2(const char*, sqlite3**, int, const char*)</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The handle comes back even when the result is a failure</b>, and
    /// it must still be closed — that is the only way to reach
    /// <c>sqlite3_errmsg</c> for the open's own error. The single case where it
    /// does not is a failure to allocate the handle at all, which is why
    /// <see cref="SqliteDatabase"/> checks <c>IsInvalid</c> before asking the
    /// handle anything.
    /// </remarks>
    /// <param name="filename">The path, or <c>:memory:</c>.</param>
    /// <param name="database">The handle, valid or not.</param>
    /// <param name="flags">The <c>SQLITE_OPEN_*</c> set.</param>
    /// <param name="vfs">The VFS name, or <see langword="null"/> for the default.</param>
    /// <returns><see cref="Ok"/>, or the failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_open_v2", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int OpenV2(string filename, out SqliteDatabaseHandle database, int flags, string? vfs);

    /// <summary>
    /// <c>int sqlite3_close_v2(sqlite3*)</c> — releases the connection, deferring
    /// if statements are still live.
    /// </summary>
    /// <remarks>
    /// <b><c>close_v2</c> and never <c>close</c>.</b> The older entry point
    /// refuses with <c>SQLITE_BUSY</c> when an unfinalized statement remains and
    /// leaks the connection; this one marks it a zombie and finishes the job
    /// when the last statement goes. Handles here are released by the garbage
    /// collector in an order nothing guarantees, so the refusing variant would
    /// leak on exactly the path nobody tests.
    /// </remarks>
    /// <param name="database">The raw connection pointer.</param>
    /// <returns><see cref="Ok"/>.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_close_v2")]
    internal static partial int CloseV2(IntPtr database);

    /// <summary>
    /// <c>const char *sqlite3_errmsg(sqlite3*)</c> — what went wrong on this
    /// connection, in English.
    /// </summary>
    /// <param name="database">The connection.</param>
    /// <returns>A pointer to UTF-8 owned by the connection.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_errmsg")]
    internal static partial IntPtr ErrorMessage(SqliteDatabaseHandle database);

    /// <summary>
    /// <c>int sqlite3_busy_timeout(sqlite3*, int)</c> — how long a blocked
    /// connection retries before answering <see cref="Busy"/>.
    /// </summary>
    /// <param name="database">The connection.</param>
    /// <param name="milliseconds">The budget.</param>
    /// <returns><see cref="Ok"/>, or the failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_busy_timeout")]
    internal static partial int BusyTimeout(SqliteDatabaseHandle database, int milliseconds);

    /// <summary>
    /// <c>int sqlite3_exec(sqlite3*, const char*, callback, void*, char**)</c> —
    /// runs one or more statements that produce nothing.
    /// </summary>
    /// <remarks>
    /// <b>The error message pointer is <see cref="IntPtr.Zero"/> on purpose.</b>
    /// SQLite allocates that string with its own allocator and requires
    /// <c>sqlite3_free</c> to release it, which would be a twenty-second entry
    /// point declared for a string <c>sqlite3_errmsg</c> already gives from the
    /// connection.
    /// </remarks>
    /// <param name="database">The connection.</param>
    /// <param name="sql">The statements, semicolon-separated.</param>
    /// <param name="callback">Always <see cref="IntPtr.Zero"/> here.</param>
    /// <param name="argument">Always <see cref="IntPtr.Zero"/> here.</param>
    /// <param name="errorMessage">Always <see cref="IntPtr.Zero"/> here.</param>
    /// <returns><see cref="Ok"/>, or the failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_exec", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Exec(SqliteDatabaseHandle database, string sql, IntPtr callback, IntPtr argument, IntPtr errorMessage);

    /// <summary>
    /// <c>sqlite3_int64 sqlite3_last_insert_rowid(sqlite3*)</c>.
    /// </summary>
    /// <param name="database">The connection.</param>
    /// <returns>The rowid of the most recent successful insert on it.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_last_insert_rowid")]
    internal static partial long LastInsertRowId(SqliteDatabaseHandle database);

    /// <summary>
    /// <c>int sqlite3_changes(sqlite3*)</c>.
    /// </summary>
    /// <param name="database">The connection.</param>
    /// <returns>How many rows the most recent statement changed.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_changes")]
    internal static partial int Changes(SqliteDatabaseHandle database);

    /// <summary>
    /// <c>int sqlite3_prepare_v2(sqlite3*, const char*, int, sqlite3_stmt**, const char**)</c>.
    /// </summary>
    /// <remarks>
    /// <b>The tail pointer is <see cref="IntPtr.Zero"/> and the byte count is
    /// <c>-1</c>.</b> One statement per prepare is the rule this layer keeps:
    /// anything with a second statement in it goes through <see cref="Exec"/>,
    /// so there is no tail anybody would read, and a caller that hands two
    /// statements to a prepare silently runs only the first — which is the one
    /// mistake the absent tail makes findable rather than silent, because
    /// nothing here ever reports a remainder.
    /// </remarks>
    /// <param name="database">The connection.</param>
    /// <param name="sql">One statement.</param>
    /// <param name="byteCount">Always <c>-1</c> here: read to the terminator.</param>
    /// <param name="statement">The compiled statement, or an invalid handle.</param>
    /// <param name="tail">Always <see cref="IntPtr.Zero"/> here.</param>
    /// <returns><see cref="Ok"/>, or the failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_prepare_v2", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int PrepareV2(SqliteDatabaseHandle database, string sql, int byteCount, out SqliteStatementHandle statement, IntPtr tail);

    /// <summary>
    /// <c>int sqlite3_step(sqlite3_stmt*)</c>.
    /// </summary>
    /// <param name="statement">The compiled statement.</param>
    /// <returns><see cref="Row"/>, <see cref="Done"/>, or a failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_step")]
    internal static partial int Step(SqliteStatementHandle statement);

    /// <summary>
    /// <c>int sqlite3_finalize(sqlite3_stmt*)</c> — destroys the statement.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Its result is the last <c>step</c>'s error and not this call's.</b>
    /// The statement is destroyed either way, which is why
    /// <see cref="SqliteStatementHandle.ReleaseHandle"/> answers
    /// <see langword="true"/> unconditionally: the resource went, and reporting
    /// a stale step failure as a failed release would be a lie about a
    /// different thing.
    /// </remarks>
    /// <param name="statement">The raw statement pointer.</param>
    /// <returns>The last step's result.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_finalize")]
    internal static partial int FinalizeStatement(IntPtr statement);

    /// <summary>
    /// <c>int sqlite3_bind_text(sqlite3_stmt*, int, const char*, int, void(*)(void*))</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The byte count is explicit and never <c>-1</c>, and that is the
    /// difference between storing a caller's text and storing a prefix of
    /// it.</b> With <c>-1</c> SQLite reads to the first zero byte, so a value
    /// carrying U+0000 — which <c>StringMarshalling.Utf8</c> encodes as a plain
    /// <c>0x00</c> — is silently truncated at it. This layer stores what it was
    /// given, so it encodes the text itself and says how long it is.
    /// </remarks>
    /// <param name="statement">The compiled statement.</param>
    /// <param name="index">The one-based parameter index.</param>
    /// <param name="value">UTF-8 bytes, zero-terminated so the pointer is never null.</param>
    /// <param name="byteCount">How many of them are the value, terminator excluded.</param>
    /// <param name="destructor">Always <see cref="Transient"/> here.</param>
    /// <returns><see cref="Ok"/>, or the failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_bind_text")]
    internal static partial int BindText(SqliteStatementHandle statement, int index, byte[] value, int byteCount, IntPtr destructor);

    /// <summary>
    /// <c>int sqlite3_bind_blob(sqlite3_stmt*, int, const void*, int, void(*)(void*))</c>.
    /// </summary>
    /// <param name="statement">The compiled statement.</param>
    /// <param name="index">The one-based parameter index.</param>
    /// <param name="value">The bytes, never a zero-length array.</param>
    /// <param name="byteCount">How many of them are the value.</param>
    /// <param name="destructor">Always <see cref="Transient"/> here.</param>
    /// <returns><see cref="Ok"/>, or the failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_bind_blob")]
    internal static partial int BindBlob(SqliteStatementHandle statement, int index, byte[] value, int byteCount, IntPtr destructor);

    /// <summary>
    /// <c>int sqlite3_bind_int64(sqlite3_stmt*, int, sqlite3_int64)</c>.
    /// </summary>
    /// <param name="statement">The compiled statement.</param>
    /// <param name="index">The one-based parameter index.</param>
    /// <param name="value">The value.</param>
    /// <returns><see cref="Ok"/>, or the failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_bind_int64")]
    internal static partial int BindInt64(SqliteStatementHandle statement, int index, long value);

    /// <summary>
    /// <c>int sqlite3_bind_null(sqlite3_stmt*, int)</c>.
    /// </summary>
    /// <param name="statement">The compiled statement.</param>
    /// <param name="index">The one-based parameter index.</param>
    /// <returns><see cref="Ok"/>, or the failure.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_bind_null")]
    internal static partial int BindNull(SqliteStatementHandle statement, int index);

    /// <summary>
    /// <c>int sqlite3_column_type(sqlite3_stmt*, int)</c>.
    /// </summary>
    /// <param name="statement">The stepped statement.</param>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>One of the <c>Type*</c> constants.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_column_type")]
    internal static partial int ColumnType(SqliteStatementHandle statement, int index);

    /// <summary>
    /// <c>sqlite3_int64 sqlite3_column_int64(sqlite3_stmt*, int)</c>.
    /// </summary>
    /// <param name="statement">The stepped statement.</param>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The value.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_column_int64")]
    internal static partial long ColumnInt64(SqliteStatementHandle statement, int index);

    /// <summary>
    /// <c>const unsigned char *sqlite3_column_text(sqlite3_stmt*, int)</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Call this before <see cref="ColumnBytes"/> for the same column.</b>
    /// SQLite converts the value in place on the first typed accessor and
    /// <c>sqlite3_column_bytes</c> reports the length <i>after</i> that
    /// conversion, so asking for the length first can measure the value the
    /// column used to hold.
    /// </remarks>
    /// <param name="statement">The stepped statement.</param>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>A pointer to UTF-8 owned by the statement.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_column_text")]
    internal static partial IntPtr ColumnText(SqliteStatementHandle statement, int index);

    /// <summary>
    /// <c>const void *sqlite3_column_blob(sqlite3_stmt*, int)</c>.
    /// </summary>
    /// <remarks>
    /// It answers <see cref="IntPtr.Zero"/> for a zero-length blob as well as
    /// for SQL <c>NULL</c>, so the two are told apart by
    /// <see cref="ColumnType"/> and never by the pointer.
    /// </remarks>
    /// <param name="statement">The stepped statement.</param>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>A pointer to bytes owned by the statement.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_column_blob")]
    internal static partial IntPtr ColumnBlob(SqliteStatementHandle statement, int index);

    /// <summary>
    /// <c>int sqlite3_column_bytes(sqlite3_stmt*, int)</c>.
    /// </summary>
    /// <param name="statement">The stepped statement.</param>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The length in bytes of what the last typed accessor returned.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Module, EntryPoint = "sqlite3_column_bytes")]
    internal static partial int ColumnBytes(SqliteStatementHandle statement, int index);
}

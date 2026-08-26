<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# `Storage\` — the record, and the guard that is not part of it

**Two files answer two questions and neither answers the other's.** `browserai.lock` says *who owns this directory*; `browserai.data` says *what happened in it*. They are separate because a transaction cannot be both the lock and the write path: a reader only sees committed work, and committing ends the transaction — so a store that tried to be the guard would either hide the log from every reader or lock them out of it. Every rule below either names what enforces it or says plainly that nothing does.

**Nothing in the product is wired to any of this yet.** As of 2026-08-26 the layer stands alone: `SessionLock`, `SessionManager` and `browserai.json` are untouched, and the only product caller is `Program.Main`'s startup record. The cutover is its own phase.

## The six properties, and where each one lives

The constraint any storage proposal has to meet, and the reason the guard did not move into SQLite:

| # | Property | What produces it |
|---|---|---|
| 1 | One writer per directory, across processes | `LockFile.Hold`'s `FileShare.Read`. A second `ReadWrite` open is refused by the kernel |
| 2 | Unlimited readers, **seeing live data** | The same share mode admits readers — and they read `browserai.data`, which is a different file, so the guard never has to admit anybody to itself |
| 3 | Held for the session's whole life | The `FileStream` is a field of `LockFileHold` and nothing closes it. **Written once at acquisition and never rewritten**, which is what removes the per-call unheld window the old record opened |
| 4 | Released by the OS on death, however it dies | A handle. No expiry, no heartbeat |
| 5 | Cheaply observable by a third process | `LockFile.Probe` — one `CreateFile`, no directory walk, no process open, no database |
| 6 | Names the holder | The lock file's content: pid, process-creation FILETIME, and a display-only client name |

## Rules a mechanism enforces

- **The probe's three answers are three answers.** A sharing violation is `Held`; a file that opens is `Released`; no file is `Free`. `LockFileTests.TheProbeTellsFreeFromHeldFromReleased` holds all three, and `.AProbeThatCannotAnswerSaysSoRatherThanSayingFree` holds the fourth: anything else is `Undetermined` **and never free**.
- **A holder is `(pid, creationFileTime)`, never a bare pid.** `LockFile.Parse` refuses a record missing the FILETIME rather than defaulting it, because Windows reuses pids within seconds and a reclaim on a bare pid takes a live stranger's directory. `LockFileTests.ALockFileThatIsNotOursIsRefusedRatherThanGuessedAt`.
- **An unknown property in a lock file is a refusal.** The set of things it may say is closed, and *this is not ours* has to stay a different answer from *this directory is free*. Same test.
- **Every bind passes an explicit byte count and every read takes its length from `sqlite3_column_bytes`.** The obvious spelling — `StringMarshalling.Utf8` and `-1` — reads to the first zero byte, and a stored value carrying `U+0000` is then silently a prefix of itself. `SqliteStorageTests.AStoredValueCarryingAZeroByteComesBackWhole`.
- **Every buffer is bound `SQLITE_TRANSIENT`, never `SQLITE_STATIC`.** The buffers are managed arrays pinned for one call; telling SQLite they are static hands it a pointer the collector may invalidate. Nothing enforces the choice mechanically — `Sqlite.Transient` is the only destructor constant declared, which is the nearest thing to one.
- **The store is WAL, carries `PRAGMA user_version`, and gets the per-directory gate's patience as its busy timeout.** `SqliteStorageTests.AFreshStoreIsVersionOneInWalModeWithTheDirectoryGatesPatience`, with the timeout compared against `LockScopes.PerDirectoryGate` rather than against a number.
- **There is no schema converter, and a foreign version is refused with the version as the reason.** `SqliteStorageTests.AStoreFromAnotherSchemaIsRefusedWithTheVersionAsTheReason`.
- **No caps, anywhere** — not on a value's length, not on the number of rows. `SqliteStorageTests.NothingInTheStoreIsCappedByLengthOrByCount`.
- **The compile-time options the build passes and the options the product claims are held together in both directions**, and the *values* are checked against the archive ILC actually linked. `SqliteTests.TheIntendedCompileOptionsAreTheOnesTheBuildPasses` and `.TheStaticallyLinkedLibraryReportsTheIntendedBuild`, the second reading `sqliteBuild=` off the published binary's own startup record.
- **The `Microsoft.Data.Sqlite` meta package may never be referenced anywhere.** It would silently regress the native SQLite to the one its bundle pins. `ForbiddenDependencyTests.TheSqliteMetaPackageIsNeverReferenced`.
- **The vendored amalgamation makes the published binary stale.** `SqliteTests.TheFreshnessCheckWatchesTheVendoredAmalgamation`, over `PublishedSlice.FreshnessInputs` rather than over the check — a staleness check is silent about what it never looked at.

## Rules nothing enforces — these need a person

- ⚠️ **`sqlite3_initialize()` must precede the first open, and forgetting it is not an error you can catch.** `SQLITE_OMIT_AUTOINIT` is in the compile flags; measured 2026-08-26, an open without it takes an **access violation** in the published binary — `0xC0000005`, exit `-1073741819` — rather than answering `SQLITE_MISUSE`. Every open goes through `SqliteDatabase.Open`, which calls `Sqlite.EnsureInitialized` first; **nothing stops a new caller reaching a declaration directly**, and the failure would be a dead server rather than a red test, because a CoreCLR test host binds a loose DLL built without the flag and initialises itself.
- ⚠️ **Every `[LibraryImport]` here carries `DllImportSearchPath.SafeDirectories`, and it has to be repeated on each one.** The attribute is valid on an assembly or a method and nothing between, so the type-level declaration that would make mixing unexpressible does not compile. Mixing matters because the resolved library is cached per module name: once any declaration naming `e_sqlite3` has loaded it, a wrong one beside it succeeds too, so a file with two values tests clean and fails on whichever runs first. **`System32` is the wrong value here** and is right under `Interop\`; see that directory's own rules.
- **The writer is the lock holder.** An application-level invariant kept by the code path, not defended: adversarial and hostile-caller defence is an explicit non-goal of this product.
- **One statement per `Prepare`, and anything with a semicolon in the middle goes through `Execute`.** `sqlite3_prepare_v2` compiles the first statement and silently ignores the rest, and the tail pointer this layer passes is null, so nothing would report the remainder.
- ⚠️ **Reading a session directory is not side-effect-free.** A read-only open against a crashed holder's uncheckpointed write-ahead log builds a `-shm` beside the store and recovers the log — `SQLITE_OPEN_READONLY` constrains the database file and not the directory. It is refused only where the caller may not create that file. Both arms are pinned in `SqliteStorageTests`; what nothing can enforce is that a future caller does not assert a session directory's file list after reading it.

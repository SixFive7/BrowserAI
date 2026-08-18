<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# `Interop\` — the whole P/Invoke surface

**All 41 `[LibraryImport]` declarations in the product are in these nine files, and nothing outside this directory calls Win32 at all** (counted 2026-08-18). The rules below hold for every file here; each says what enforces it, and three of them are enforced by nothing.

- **Every declaration carries `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`.** All 41 do; **nothing asserts it**. Without it a DLL of the same name sitting beside the executable wins the search, and the call you get is somebody else's.
- **A hand-written struct is checked against Microsoft's own metadata, or it is not written.** `InteropLayoutTests` is the oracle, reached through `CsWin32` and the names in `tests/BrowserAI.Tests/NativeMethods.txt`. **A struct added without a line in that file is unchecked and looks exactly like a checked one** — and this is the one defect class here that cannot present as an error: a field that slid four bytes makes `SetInformationJobObject` return success and do something else.
- **A process is `(pid, creationFileTime)`, never a bare pid; a browser is found by full image path, never by image name.** [`build/BannedSymbols.txt`](../../../build/BannedSymbols.txt) bans the framework's name-based calls and `NeverByImageNameTests` reads the tree for `taskkill /IM`, a WMI `Name` filter and a toolhelp `szExeFile` walk. **Neither can see a new native declaration added here**, which is exactly why this line is in this file: pids are reused within seconds, and the user has around forty `firefox.exe` of their own.
- **Read `Marshal.GetLastPInvokeError()` before anything else runs.** A `Dispose`, a log call or an allocating `if` between the call and the read replaces the error with somebody else's, and the message you then report is wrong rather than missing. **Nothing asserts this.**
- **`AllowUnsafeBlocks` is on for P/Invoke declarations, never for hand-written pointer code.** Both project files say so in a comment; **nothing asserts it**, and the day it is used for anything else is the day this directory stops being a translation layer.

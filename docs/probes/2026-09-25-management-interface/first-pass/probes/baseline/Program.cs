// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The cost of a NativeAOT process that announces itself and exits.
Shared.Announce(args[0], 0);

// Touch the body so the linker keeps what the others keep.
return Shared.StateJson.Length > 0 ? 0 : 1;

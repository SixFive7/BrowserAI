// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI;

/// <summary>
/// Entry point.
/// </summary>
/// <remarks>
/// Deliberately empty at build-order step 1, which is the skeleton: this
/// project exists so the toolchain, the analyzers and the NativeAOT publish
/// can be proven before there is any code to get wrong. stdout ownership
/// arrives at step 2 and the MCP server itself at step 7 -- until then, an exit
/// code of 0 from the published native binary is the whole contract.
/// </remarks>
internal static class Program
{
    private static int Main() => 0;
}

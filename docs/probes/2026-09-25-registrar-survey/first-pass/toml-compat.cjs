// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Which TOML shapes that Codex (Rust toml, TOML 1.0) accepts can the registrars' TOML libraries read and write back?
const libs = {
  "@iarna/toml 2.2.5 (add-mcp, install-mcp)": require("./pkgs/add-mcp/node_modules/@iarna/toml"),
};
const cases = {
  "mixed-type array (TOML 1.0)": '[mcp_servers.x]\ncommand = "a"\nargs = ["--port", 8080]\n',
  "dotted keys": 'mcp_servers.x.command = "a"\n',
  "multi-line literal string, as Codex writes an apostrophe path": "[mcp_servers.b]\ncommand = '''C:\\Users\\Zo\u00eb O'Brien\\x.exe'''\n",
  "inline table": '[mcp_servers.x]\ncommand = "a"\nenv = { A = "1" }\n',
  "comment and trailing comment": '# keep me\n[mcp_servers.x]\ncommand = "a" # and me\n',
};
for (const [name, lib] of Object.entries(libs)) {
  for (const [k, v] of Object.entries(cases)) {
    try {
      const o = lib.parse(v);
      const back = lib.stringify(o);
      console.log("PARSE OK  ", name, "|", k, "|", JSON.stringify(o), "| round-trip keeps comments:", back.includes("#"));
    } catch (e) {
      console.log("PARSE FAIL", name, "|", k, "->", String(e.message).split("\n")[0]);
    }
  }
}

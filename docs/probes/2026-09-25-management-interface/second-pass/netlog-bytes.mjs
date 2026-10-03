// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Counts the events that mean bytes actually left a socket, and lists DNS transactions.
import fs from "node:fs";
for (const file of process.argv.slice(2)) {
  let text = fs.readFileSync(file, "utf8");
  let log; try { log = JSON.parse(text); } catch { log = JSON.parse(text.trimEnd().replace(/,\s*$/, "") + "]}"); }
  const typeName = Object.fromEntries(Object.entries(log.constants.logEventTypes).map(([k, v]) => [v, k]));
  const counts = {}; const dns = new Set(); const udpSent = new Map(); const src = new Map();
  for (const e of log.events) {
    const n = typeName[e.type]; const p = e.params ?? {};
    if (/BYTES_SENT|BYTES_RECEIVED|TCP_CONNECT$|SSL_CONNECT$|DNS_TRANSACTION$|DNS_TRANSACTION_QUERY|QUIC_SESSION$|UDP_CONNECT$|TCP_CONNECT_ATTEMPT/.test(n)) counts[n] = (counts[n] ?? 0) + 1;
    if (n === "UDP_CONNECT" && p.address) src.set(e.source.id, p.address);
    if (n === "UDP_BYTES_SENT") udpSent.set(src.get(e.source.id) ?? "?", (udpSent.get(src.get(e.source.id) ?? "?") ?? 0) + (p.byte_count ?? 0));
    if ((n === "DNS_TRANSACTION" || n === "HOST_RESOLVER_DNS_TASK" || n === "HOST_RESOLVER_SYSTEM_TASK" || n === "HOST_RESOLVER_MANAGER_JOB") && (p.hostname || p.host)) dns.add(`${n}:${p.hostname ?? p.host}${p.query_type ? "/" + p.query_type : ""}`);
  }
  console.log("== " + file);
  console.log("   counts: " + JSON.stringify(counts));
  console.log("   udp bytes sent per address: " + JSON.stringify([...udpSent.entries()]));
  console.log("   name lookups: " + [...dns].join(" | "));
}

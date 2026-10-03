// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Summarises a Chromium --log-net-log file: every URL the network stack was asked for,
// every host name handed to the resolver, and every socket address it tried.
//   node netlog-summary.mjs <netlog.json> [out.tsv]
import fs from "node:fs";

const [file, out] = process.argv.slice(2);
let text = fs.readFileSync(file, "utf8");
let log;
try {
  log = JSON.parse(text);
} catch {
  text = text.trimEnd().replace(/,\s*$/, "") + "]}";
  log = JSON.parse(text);
}
const typeName = Object.fromEntries(Object.entries(log.constants.logEventTypes).map(([k, v]) => [v, k]));
const errName = Object.fromEntries(Object.entries(log.constants.netError).map(([k, v]) => [v, k]));
const urls = new Map(), hosts = new Map(), addrs = new Map(), errors = new Map(), firstSeen = new Map();
const t0 = Number(log.events[0]?.time ?? 0);
const bump = (m, k, t) => { m.set(k, (m.get(k) ?? 0) + 1); if (!firstSeen.has(k)) firstSeen.set(k, ((Number(t) - t0) / 1000).toFixed(1)); };
const urlSource = new Map();
for (const e of log.events) {
  const name = typeName[e.type];
  const p = e.params ?? {};
  if ((name === "URL_REQUEST_START_JOB" || name === "REQUEST_ALIVE") && p.url) {
    const u = String(p.url).replace(/\?.*$/, "?...");
    if (name === "URL_REQUEST_START_JOB") { bump(urls, u, e.time); urlSource.set(e.source.id, u); }
  }
  if (name === "HOST_RESOLVER_MANAGER_REQUEST" && p.host) bump(hosts, String(p.host), e.time);
  if ((name === "TCP_CONNECT_ATTEMPT" || name === "UDP_CONNECT" || name === "SOCKS_CONNECT") && p.address) bump(addrs, `${name} ${p.address}`, e.time);
  if (name === "UDP_LOCAL_ADDRESS" && p.address) bump(addrs, `UDP_LOCAL ${String(p.address).replace(/:\d+$/, ":*")}`, e.time);
  if (name === "URL_REQUEST_START_JOB" || name === "URL_REQUEST_DELEGATE_RESPONSE_STARTED") continue;
  if ((name === "FAILED" || name === "URL_REQUEST_JOB_FILTERED_BYTES_READ") && p.net_error && urlSource.has(e.source.id)) bump(errors, `${urlSource.get(e.source.id)} -> ${errName[p.net_error] ?? p.net_error}`, e.time);
}
const lines = [];
const dump = (title, m) => { lines.push(`# ${title} (${m.size} distinct)`); for (const [k, v] of [...m.entries()].sort((a, b) => b[1] - a[1])) lines.push(`${v}\tfirst@${firstSeen.get(k)}s\t${k}`); };
lines.push(`# file ${file}; events ${log.events.length}; span ${((Number(log.events.at(-1).time) - t0) / 1000).toFixed(0)} s`);
dump("URLs the network stack was asked for", urls);
dump("host names handed to the resolver", hosts);
dump("socket addresses tried", addrs);
dump("requests that ended in an error", errors);
const text2 = lines.join("\n") + "\n";
if (out) fs.writeFileSync(out, text2);
process.stdout.write(text2);

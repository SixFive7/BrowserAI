// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

(() => {
  const M = MOCK;
  const D = M.data;
  const I = ICON;
  const esc = M.esc;
  M.applyTheme();

  const main = document.getElementById('main');
  const drawer = document.getElementById('drawer');
  const count = (ms) => '<span class="count" data-ends="' + ms + '"></span>';
  const plural = (n, one, many) => n + ' ' + (n === 1 ? one : many);
  const appIc = (app) => '<span class="app-ic ' + (app === 'Codex' ? 'cx' : 'cc') + '" aria-hidden="true">' + (app === 'Codex' ? 'CX' : 'CC') + '</span>';
  const cls = (s) => (s.state === 'window' ? 'w' : s.state === 'hidden' ? 'h' : 'c');
  const stateText = (s) => s.state === 'window' ? (s.never ? 'Window on your screen, never closes' : 'Window on your screen, ' + count(s.closes)) : s.state === 'hidden' ? 'Hidden, closes in ' + count(s.closes) : 'Closed, folder kept';
  const placeLabel = (c) => c.place === 'VS Code' ? 'Claude Code in VS Code' : c.place === 'Terminal' ? 'Claude Code in a terminal' : 'Codex';

  function sessionNode(s) {
    return '<button class="node ' + cls(s) + '" type="button" data-open="s:' + s.id + '"><span class="br-ic ' + cls(s) + '">' + I.of(s.state === 'window' ? 'window' : s.state === 'hidden' ? 'hidden' : 'closed') + '</span><span class="txt"><b>' + esc(s.name) + '</b><small>' + esc(s.browser) + (s.kept ? ', kept' : '') + '</small><span class="state">' + stateText(s) + '</span></span></button>';
  }

  function conversationNode(c) {
    return '<button class="node" type="button" data-open="c:' + c.id + '">' + appIc(c.app) + '<span class="txt"><b>' + esc(c.title) + '</b><small>' + esc(placeLabel(c)) + ', ' + esc(c.folderName) + '</small></span>' + (c.running ? '<span class="tag live">Working</span>' : '') + '</button>';
  }

  function alerts() {
    let html = '';
    for (const a of M.attention) {
      html += '<div class="alert ' + a.level + '" role="alert">' + I.of(a.level === 'bad' ? 'bad' : 'warn') + '<p><b>' + esc(a.title) + '.</b> ' + esc(a.text) + '</p>'
        + (a.kind === 'broken' ? '<button class="btn" type="button" data-open="hub">How to reinstall</button>' : a.kind === 'app' ? '<a class="btn" href="#/setup">Repair</a>' : '<a class="btn" href="#/update">See the update</a>') + '</div>';
    }
    if (D.occasion === 'firstrun') {
      html += '<div class="alert good">' + I.of('checkCircle') + '<p><b>BrowserAI is installed.</b> Claude Code is set up for all your projects, and Codex was not found. When a conversation uses BrowserAI, it appears on this map with the browser it opens.</p><a class="btn" href="#/setup">See the setup</a></div>';
    }
    return html ? '<div class="alerts">' + html + '</div>' : '';
  }

  // ---- The map --------------------------------------------------------------------

  function map() {
    const rows = [];
    let lastWindow = null;
    for (const c of D.conversations) {
      const label = c.windowKey && c.windowKey !== lastWindow ? '<div class="group-label">' + I.of('code') + esc(c.windowLabel) + '</div>' : !c.windowKey ? '<div class="group-label">' + I.of(c.place === 'Terminal' ? 'terminal' : 'chat') + esc(c.place === 'Terminal' ? 'Terminal in ' + c.folderName : 'Codex app') + '</div>' : '';
      lastWindow = c.windowKey || null;
      rows.push({ key: c.id, left: '<div class="left-cell" data-row="' + c.id + '">' + label + conversationNode(c) + '</div>', right: c.sessions.length ? '<div class="right-cell" data-row="' + c.id + '">' + c.sessions.map((id) => sessionNode(M.sessionById(id))).join('') + '</div>' : '<div class="none-cell" data-row="' + c.id + '">No browser yet</div>' });
    }
    for (const s of D.sessions.filter((x) => x.kept)) {
      rows.push({ key: s.id, left: '<div class="ghost" data-row="' + s.id + '">Its conversation ended. BrowserAI keeps the session for the next one.</div>', right: '<div class="right-cell" data-row="' + s.id + '">' + sessionNode(s) + '</div>' });
    }

    const u = D.update;
    const hub = '<button class="hub" type="button" data-open="hub">' + I.brand('hub') + '<h2>BrowserAI ' + esc(D.version) + '</h2><p>' + (D.broken ? '<b class="hub-bad">Needs reinstalling</b>' : 'Running since ' + esc(D.since)) + '</p>'
      + '<div class="hub-stat"><div><span>Conversations</span><b>' + D.conversations.length + '</b></div><div><span>Open browsers</span><b>' + M.openBrowsers + '</b></div><div><span>On your screen</span><b>' + M.windows.length + '</b></div></div>'
      + (u.state === 'held' ? '<div class="upd"><b>Update ' + esc(u.version) + ' ready</b>' + (u.wait === 'counting' ? 'installs in ' + count(u.installsAt) + ' if nothing uses BrowserAI' : 'waits for a window to close') + '</div>' : '<div class="upd ok"><b>Up to date</b>checked at ' + esc(u.checked) + '</div>') + '</button>';

    let grid = '<div class="col-head">Conversations</div><div></div><div class="col-head">Where they run</div><div></div><div class="col-head right">Browser sessions</div>';
    if (rows.length === 0) {
      grid += '<div class="empty-map"><b>Nothing is connected</b>When an agent in Claude Code or Codex calls a BrowserAI tool, its conversation appears on the left, with a line to the browser it opens on the right.</div>';
      grid += hub.replace('class="hub"', 'class="hub" data-alone="1"');
    } else {
      grid += hub + rows.map((r, i) => r.left.replace('class="', 'data-i="' + i + '" class="') + r.right.replace('class="', 'data-i="' + i + '" class="')).join('');
    }

    const busy = D.conversations.length;
    const title = M.attention.length ? plural(M.attention.length, 'thing needs you', 'things need you') : busy ? plural(busy, 'conversation is', 'conversations are') + ' using ' + plural(M.openBrowsers, 'browser', 'browsers') : 'Nothing is using BrowserAI right now';
    const sub = busy ? 'Each line runs from a conversation in Claude Code or Codex, through BrowserAI, to the browser session it opened. Click anything to see what it is.' : 'BrowserAI is running and ready. This map fills in as soon as an agent uses it.';

    return '<div class="intro"><div><h1>' + esc(title) + '</h1><p>' + esc(sub) + '</p></div><div class="legend"><span><i class="w"></i>Window on your screen</span><span><i class="h"></i>Hidden browser</span><span><i class="c"></i>No browser open</span><span><i class="k"></i>Kept, no conversation</span></div></div>'
      + alerts()
      + '<div class="map" id="map"><svg class="wires" id="wires" aria-hidden="true"></svg>' + grid + '</div>'
      + '<div class="below">'
      + '<a class="mini" href="#/update">' + I.of('update') + '<div><b>' + (u.state === 'held' ? 'Update ' + esc(u.version) + ' is ready' : 'Up to date') + '</b><span>' + (u.state === 'held' ? 'What holds it, and Install now' : 'Checked ' + esc(u.source) + ' at ' + esc(u.checked)) + '</span></div>' + I.of('chevron', 'chev') + '</a>'
      + '<a class="mini" href="#/setup">' + I.of('plug') + '<div><b>Claude Code and Codex</b><span>' + (D.apps.every((a) => a.state === 'ok') ? 'Both set up for all your projects' : D.apps.map((a) => esc(a.name) + ': ' + esc(a.short.toLowerCase())).join(', ')) + '</span></div>' + I.of('chevron', 'chev') + '</a>'
      + '<a class="mini" href="#/storage">' + I.of('disk') + '<div><b>' + M.size(M.totalMb) + ' on this PC</b><span>' + plural(M.folderCount, 'session folder', 'session folders') + ', browsers and logs</span></div>' + I.of('chevron', 'chev') + '</a>'
      + '</div>';
  }

  // Draws the wires once the cards have their places.
  function drawWires() {
    const svg = document.getElementById('wires');
    const mapEl = document.getElementById('map');
    const hubEl = mapEl && mapEl.querySelector('.hub');
    if (!svg || !hubEl || hubEl.dataset.alone) return;
    const box = mapEl.getBoundingClientRect();
    const hub = hubEl.getBoundingClientRect();
    const hx1 = hub.left - box.left;
    const hx2 = hub.right - box.left;
    let paths = '';
    const port = (x, y, k) => '<circle class="port ' + k + '" cx="' + x + '" cy="' + y + '" r="5"/>';
    const curve = (x1, y1, x2, y2) => { const mx = (x1 + x2) / 2; return 'M' + x1 + ' ' + y1 + ' C' + mx + ' ' + y1 + ' ' + mx + ' ' + y2 + ' ' + x2 + ' ' + y2; };
    for (const left of mapEl.querySelectorAll('.left-cell, .ghost')) {
      const key = left.dataset.row;
      const conv = M.conversationById(key);
      const node = left.querySelector('.node') || left;
      const ln = node.getBoundingClientRect();
      const y = ln.top + ln.height / 2 - box.top;
      const right = mapEl.querySelector('.right-cell[data-row="' + key + '"]');
      const sessions = right ? [...right.querySelectorAll('.node')] : [];
      const first = conv && conv.sessions.length ? M.sessionById(conv.sessions[0]) : M.sessionById(key);
      const k = conv ? (first ? cls(first) : 'n') : 'k';
      const live = conv && conv.running ? ' live' : '';
      if (conv) {
        paths += '<path class="wire ' + k + live + '" d="' + curve(ln.right - box.left, y, hx1, y) + '"/>' + port(hx1, y, k);
      }
      for (const sn of sessions) {
        const r = sn.getBoundingClientRect();
        const ty = r.top + r.height / 2 - box.top;
        const s = M.sessionById(sn.dataset.open.slice(2));
        const sk = conv ? cls(s) : 'k';
        paths += '<path class="wire ' + sk + live + '" d="' + curve(hx2, y, r.left - box.left, ty) + '"/>' + port(hx2, y, sk);
      }
      if (!right && conv) {
        const none = mapEl.querySelector('.none-cell[data-row="' + key + '"]');
        if (none) {
          const r = none.getBoundingClientRect();
          paths += '<path class="wire c" d="' + curve(hx2, y, r.left - box.left - 6, y) + '"/>' + port(hx2, y, 'n');
        }
      }
    }
    svg.setAttribute('viewBox', '0 0 ' + box.width + ' ' + box.height);
    svg.innerHTML = paths;
  }

  // ---- The inspector ------------------------------------------------------------------

  function inspect(what) {
    let html = '<button class="close" type="button" data-close aria-label="Close">x</button>';
    if (what === 'hub') {
      html += '<div class="kind">' + I.of('info') + 'BrowserAI</div><h2>BrowserAI ' + esc(D.version) + '</h2>'
        + '<div class="what">' + I.of('question') + '<div>BrowserAI runs in the background on this PC from sign-in to sign-out. Claude Code and Codex start it for each conversation, and it gives the agent a real browser.</div></div>'
        + '<dl><dt>Running since</dt><dd>' + esc(D.since) + ', started ' + esc(D.startedHow) + '</dd><dt>Installed in</dt><dd><code>' + esc(D.install.root) + '</code></dd><dt>Data</dt><dd><code>' + esc(D.install.data) + '</code></dd><dt>Updates</dt><dd>from ' + esc(D.update.source) + ', checked ' + esc(D.update.checked) + '</dd></dl>'
        + (D.broken ? '<div class="alert bad">' + I.of('bad') + '<p><b>Needs reinstalling.</b> Download BrowserAI.exe from the latest release and run it. It installs over this install and keeps every session.</p></div>' : '')
        + '<div class="btns"><button class="btn" type="button">' + I.of('folder') + 'Install folder</button><button class="btn" type="button">' + I.of('folder') + 'Data</button><button class="btn" type="button">' + I.of('file') + 'Logs</button></div>';
    } else if (what.startsWith('c:')) {
      const c = M.conversationById(what.slice(2));
      html += '<div class="kind">' + I.of('chat') + 'Conversation</div><h2>' + esc(c.title) + '</h2>'
        + '<div class="what">' + I.of('question') + '<div>A chat in ' + esc(c.app) + '. It is connected to BrowserAI while it is open, and it ends when ' + esc(c.app) + ' closes it, never from here.</div></div>'
        + '<dl><dt>App</dt><dd>' + esc(c.app + ' ' + c.version) + ', ' + esc(c.place === 'Codex' ? 'desktop app' : 'in ' + c.place) + '</dd><dt>Folder</dt><dd><code>' + esc(c.folder) + '</code></dd><dt>Connected</dt><dd>since ' + esc(c.since) + '</dd><dt>Now</dt><dd>' + (c.running ? 'A call is running.' : c.holdsUntil ? 'Called a few minutes ago.' : 'Idle.') + '</dd><dt>After an update</dt><dd>' + esc(M.reconnectText[c.reconnect]) + '</dd></dl>'
        + (c.sessions.length ? '<p>Its browser sessions:</p>' + c.sessions.map((id) => sessionNode(M.sessionById(id))).join('') : '<p class="lede">No browser yet.</p>');
    } else {
      const s = M.sessionById(what.slice(2));
      const c = s.by ? M.conversationById(s.by) : null;
      html += '<div class="kind">' + I.of('globe') + 'Browser session</div><h2>' + esc(s.name) + '</h2>'
        + '<div class="what">' + I.of('question') + '<div>A folder an agent made for its browser work: the browser profile with its logins and cookies, screenshots, downloads and a log. Only the agent that made it deletes it.</div></div>'
        + '<dl><dt>For</dt><dd>' + esc(s.purpose) + '</dd><dt>Browser</dt><dd>' + esc(s.browser) + ', ' + stateText(s) + '</dd><dt>Used by</dt><dd>' + (c ? esc(c.title) : 'nobody, its conversation ended') + '</dd><dt>Folder</dt><dd><code>' + esc(s.folder) + '</code></dd><dt>Size</dt><dd>' + M.size(s.mb) + (s.flags.length ? ', holds ' + s.flags.map(esc).join(', ') : '') + '</dd></dl>'
        + '<p class="kind">Latest call</p><p class="why">' + esc(s.lastWhy) + '</p>'
        + (s.state === 'window' ? '<div class="alert warn">' + I.of('window') + '<p>This window is on your screen. While it is open, it holds updates. Closing it ends the browser and keeps the session.</p></div>' : '')
        + '<div class="btns"><button class="btn" type="button">' + I.of('folder') + 'Open folder</button></div>';
    }
    drawer.innerHTML = html;
    drawer.classList.add('open');
    for (const n of main.querySelectorAll('[aria-pressed]')) n.setAttribute('aria-pressed', 'false');
    for (const n of main.querySelectorAll('[data-open="' + what + '"]')) n.setAttribute('aria-pressed', 'true');
    M.tick(drawer);
  }

  // ---- The other tabs -----------------------------------------------------------------

  function update() {
    const u = D.update;
    if (u.state !== 'held') {
      return '<div class="page"><h1>BrowserAI is up to date</h1><p class="lede">' + esc(D.version) + ' is the newest. BrowserAI checked ' + esc(u.source) + ' at ' + esc(u.checked) + '. A newer version is downloaded by itself and installed once nothing uses BrowserAI.</p></div>';
    }
    const it = (icon, t, sub, pill) => '<div class="item">' + I.of(icon) + '<div class="txt">' + t + '<small>' + sub + '</small></div>' + pill + '</div>';
    return '<div class="page"><h1>Update ' + esc(u.version) + ' is ready to install</h1><p class="lede">It installs by itself once nothing uses BrowserAI. On the map, every coloured line holds it.</p><div class="cards">'
      + '<div class="card"><h2>' + I.of('clock') + 'Installs by itself</h2>' + (u.wait === 'counting' ? '<div class="big">' + count(u.installsAt) + '</div><p class="lede">at about ' + M.clockOf(u.installsAt) + ', if nothing is used before then</p>' : '<p>Waits for a window set never to close by itself.</p>') + '<button class="btn danger" type="button">Install ' + esc(u.version) + ' now</button><p class="lede">Installing now closes every browser and ends every connection. Claude Code in VS Code reconnects by itself; in a terminal, run /mcp, choose BrowserAI, then Reconnect; Codex needs a new conversation.</p></div>'
      + '<div class="card"><h2>' + I.of('globe') + 'Browsers that hold it</h2>' + (M.windows.map((s) => it('window', esc(s.name), 'Close it to let the update go ahead', '<span class="pill w">' + (s.never ? 'never' : count(s.closes)) + '</span>')).join('') + M.hidden.map((s) => it('hidden', esc(s.name), s.kept ? 'Kept, no conversation' : 'Closes if no call uses it', '<span class="pill h">' + count(s.closes) + '</span>')).join('') || '<p class="lede">None.</p>') + '</div>'
      + '<div class="card"><h2>' + I.of('chat') + 'Conversations that hold it</h2>' + (D.conversations.map((c) => it(c.place === 'Terminal' ? 'terminal' : 'code', esc(c.title), esc(M.reconnectText[c.reconnect]), c.running ? '<span class="pill live">working</span>' : c.holdsUntil ? '<span class="pill">' + count(c.holdsUntil) + '</span>' : '<span class="pill">idle</span>')).join('') || '<p class="lede">None.</p>') + '</div>'
      + '</div></div>';
  }

  function setup() {
    return '<div class="page"><h1>Claude Code and Codex</h1><p class="lede">Your agents reach BrowserAI through these apps. Each is set up on its own, for all your projects or in one project folder.</p><div class="cards">'
      + D.apps.map((a) => '<div class="card"><h2>' + appIc(a.name) + esc(a.name) + '</h2><p><span class="pill ' + (a.state === 'ok' ? 'h' : a.state === 'repair' ? 'bad' : '') + '">' + esc(a.short) + '</span></p><p class="lede">' + esc(a.sentence) + '</p>'
        + (a.state === 'missing' ? '<button class="btn" type="button" disabled>Install ' + esc(a.name) + ' first</button>' : '<div class="btns">' + (a.state === 'repair' ? '<button class="btn primary" type="button">Repair</button>' : '<button class="btn" type="button">Set up again</button><button class="btn" type="button">Remove</button>') + '<button class="btn" type="button">In one project</button></div>') + '</div>').join('')
      + '<div class="card"><h2>' + I.of('info') + 'What setting up does</h2><p class="lede">It writes one entry, named browserai, into the app\'s own settings. The entry starts BrowserAI.exe with --mcp when a conversation begins. A conversation that was already open sees the change only after a restart.</p></div>'
      + '</div></div>';
  }

  function storage() {
    const all = D.sessions.map((s) => ({ name: s.name, mb: s.mb, used: s.running ? 'now' : s.lastAgo, flags: s.flags })).concat(D.onDisk.map((s) => ({ name: s.name, mb: s.mb, used: s.used, flags: s.flags }))).sort((x, y) => y.mb - x.mb);
    return '<div class="page"><h1>' + M.size(M.totalMb) + ' on this PC</h1><p class="lede">Session folders stay until the agent that made them deletes them. Browsers are downloaded once and shared.</p><div class="cards">'
      + '<div class="card wide"><h2>' + I.of('folder') + plural(M.folderCount, 'session folder', 'session folders') + ', ' + M.size(M.sessionsMb) + '</h2>' + (all.map((r) => '<div class="item">' + I.of('folder') + '<div class="txt">' + esc(r.name) + r.flags.map((f) => '<span class="flag">' + esc(f) + '</span>').join('') + '<small>last used ' + esc(r.used) + '</small></div><span class="pill">' + M.size(r.mb) + '</span><button class="btn" type="button">Open</button></div>').join('') || '<p class="lede">No session folder yet.</p>') + '</div>'
      + '</div></div>';
  }

  function whatsNew() {
    const c = D.changelog;
    return '<div class="page"><h1>What is new in ' + esc(c.version) + '</h1><p class="lede">Released ' + esc(c.date) + '.</p><div class="cards">' + c.groups.map((g) => '<div class="card"><h2>' + I.of('sparkle') + esc(g.name) + '</h2>' + g.entries.map((e) => '<div class="item"><span aria-hidden="true">' + e.icon + '</span><div class="txt">' + esc(e.head) + '<small>' + esc(e.body) + '</small></div></div>').join('') + '</div>').join('') + '</div></div>';
  }

  const tabs = [
    { key: '', label: 'Map', icon: 'map', view: map },
    { key: 'update', label: 'Update', icon: 'update', view: update, dot: () => D.update.state === 'held' },
    { key: 'setup', label: 'Claude Code and Codex', icon: 'plug', view: setup, dot: () => D.apps.some((a) => a.state === 'repair') },
    { key: 'storage', label: 'Storage', icon: 'disk', view: storage },
    { key: 'new', label: 'What is new', icon: 'sparkle', view: whatsNew },
  ];

  function render() {
    const parts = location.hash.replace(/^#\/?/, '').split('/');
    const tab = tabs.find((t) => t.key === parts[0]) || tabs[0];
    main.innerHTML = tab.view();
    for (const a of document.querySelectorAll('.tabs a')) {
      if (a.dataset.key === tab.key) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    }
    drawer.classList.remove('open');
    M.tick(main);
    if (tab.key === '') {
      const mapEl = document.getElementById('map');
      const cells = [...mapEl.querySelectorAll('[data-i]')];
      const n = cells.reduce((m, el) => Math.max(m, Number(el.dataset.i) + 1), 0);
      for (const el of cells) el.style.gridRow = String(Number(el.dataset.i) + 2);
      const hubEl = mapEl.querySelector('.hub');
      if (hubEl) hubEl.style.gridRow = n ? '2 / span ' + n : '2';
      const empty = mapEl.querySelector('.empty-map');
      if (empty) empty.style.gridRow = '3';
      requestAnimationFrame(() => {
        drawWires();
        const pick = new URLSearchParams(location.search).get('inspect');
        if (pick) inspect(pick);
      });
    }
  }

  const led = M.attention.some((a) => a.level === 'bad') ? 'bad' : M.attention.length ? 'warn' : '';
  document.getElementById('bar').innerHTML = '<a class="logo" href="#/">' + I.brand('top') + 'BrowserAI</a><nav class="tabs" aria-label="Views">'
    + tabs.map((t) => '<a href="#/' + t.key + '" data-key="' + t.key + '">' + I.of(t.icon) + '<span class="lbl">' + esc(t.label) + '</span>' + (t.dot && t.dot() ? '<span class="dotn" aria-label="needs a look"></span>' : '') + '</a>').join('')
    + '</nav><span class="bar-gap"></span><span class="status"><span class="led ' + led + '"></span>' + esc(D.version) + (led ? ', needs you' : ', running') + '</span>';

  document.addEventListener('click', (e) => {
    const open = e.target.closest('[data-open]');
    if (open) { inspect(open.dataset.open); return; }
    if (e.target.closest('[data-close]')) { drawer.classList.remove('open'); }
  });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') drawer.classList.remove('open'); });
  window.addEventListener('resize', drawWires);
  window.addEventListener('hashchange', render);
  render();
  M.startTicking();
  M.mockControl();
})();

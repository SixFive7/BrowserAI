// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

(() => {
  const M = MOCK;
  const D = M.data;
  const I = ICON;
  const esc = M.esc;
  M.applyTheme();

  const root = document.getElementById('wrap');
  const count = (ms) => '<span class="count" data-ends="' + ms + '"></span>';
  const plural = (n, one, many) => n + ' ' + (n === 1 ? one : many);
  const ava = (app) => '<span class="ava ' + (app === 'Codex' ? 'cx' : 'cc') + '" aria-hidden="true">' + (app === 'Codex' ? 'CX' : 'CC') + '</span>';
  const placeLabel = (c) => c.place === 'VS Code' ? 'Claude Code in VS Code' : c.place === 'Terminal' ? 'Claude Code in a terminal' : 'Codex';
  const kinds = {
    call: { dot: 'call', icon: 'pulse', group: ['browsers', 'conversations'] },
    window: { dot: 'window', icon: 'window', group: ['browsers'] },
    open: { dot: 'open', icon: 'hidden', group: ['browsers'] },
    closed: { dot: '', icon: 'closed', group: ['browsers'] },
    kept: { dot: '', icon: 'folder', group: ['browsers'] },
    connect: { dot: 'connect', icon: 'chat', group: ['conversations'] },
    update: { dot: 'update', icon: 'update', group: ['update'] },
    start: { dot: '', icon: 'bolt', group: [] },
    problem: { dot: 'problem', icon: 'bad', group: ['problems'] },
  };

  function event(e, first) {
    const k = kinds[e.kind];
    const c = e.who ? M.conversationById(e.who) : null;
    const s = e.session ? M.sessionById(e.session) : null;
    let body = c ? '<p><span class="who">' + esc(c.title) + '</span> ' + esc(e.text) + '.</p><p class="ctx">' + esc(placeLabel(c)) + ', ' + esc(c.folderName) + '</p>' : '<p>' + esc(e.text) + '</p>';
    if (e.why) body += '<p class="why">' + esc(e.why) + '</p>';
    const acts = [];
    if (s) acts.push('<a href="#/sessions">See ' + esc(s.name) + '</a>');
    if (e.kind === 'update') acts.push('<a href="#/update">See the update</a>');
    if (e.kind === 'problem') acts.push('<a href="#/folders">How to fix it</a>');
    if (acts.length) body += '<p class="acts">' + acts.join('') + '</p>';
    return '<div class="ev' + (first ? ' latest' : '') + '" data-groups="' + k.group.join(' ') + '"><span class="t">' + esc(e.t) + '</span><span class="rail"><span class="dot ' + k.dot + '">' + I.of(k.icon) + '</span></span><div class="body">' + body + '</div></div>';
  }

  function feed() {
    const busy = D.conversations.length;
    const title = M.attention.length ? plural(M.attention.length, 'thing needs you', 'things need you') + ' today' : busy ? 'A busy afternoon' : D.occasion === 'firstrun' ? 'BrowserAI was just installed' : 'A quiet day so far';
    const sum = D.occasion === 'firstrun' ? 'It set itself up for Claude Code. When an agent uses it, what it does appears here as it happens.'
      : 'Since ' + esc(D.since) + ': ' + (busy ? plural(busy, 'conversation', 'conversations') + ' used BrowserAI. ' + plural(M.openBrowsers, 'browser is', 'browsers are') + ' open now' + (M.windows.length ? ', ' + (M.windows.length === 1 ? 'one of them a window' : M.windows.length + ' of them windows') + ' on your screen.' : ', all of them hidden.') : 'nothing has used BrowserAI for a while. It is running and ready.');
    let html = '<section><div class="head"><p class="date">Today, ' + esc(M.clockOf(Date.now())) + '</p><h1>' + esc(title) + '</h1><p>' + sum + '</p></div>'
      + '<div class="filters" role="group" aria-label="Show"><button type="button" aria-pressed="true" data-f="">Everything</button><button type="button" aria-pressed="false" data-f="browsers">Browsers</button><button type="button" aria-pressed="false" data-f="conversations">Conversations</button><button type="button" aria-pressed="false" data-f="update">Update</button><button type="button" aria-pressed="false" data-f="problems">Problems</button></div>'
      + '<div class="tl" id="tl">';
    let hour = null;
    D.activity.forEach((e, i) => {
      const h = e.t.slice(0, 2);
      if (h !== hour) {
        hour = h;
        html += '<div class="hour">' + h + ':00</div>';
      }
      html += event(e, i === 0);
    });
    return html + '</div></section>' + nowColumn();
  }

  function nowColumn() {
    let html = '<aside class="now" aria-label="Now">';
    for (const a of M.attention) {
      html += '<div class="box ' + a.level + '" role="alert"><h2>' + I.of(a.level === 'bad' ? 'bad' : 'warn') + esc(a.title) + '</h2><p class="lede">' + esc(a.text) + '</p>'
        + (a.kind === 'broken' ? '<a class="btn dark" href="#/folders">How to reinstall</a>' : a.kind === 'app' ? '<a class="btn dark" href="#/setup">Repair</a>' : '<a class="btn dark" href="#/update">See the update</a>') + '</div>';
    }
    html += '<div class="box"><h2>' + I.of('window') + 'On your screen</h2>' + (M.windows.length
      ? '<p class="lede">Browser windows an agent opened. Each closes after an hour nobody uses it.</p>' + M.windows.map((s) => '<div class="line"><div class="txt">' + esc(s.name) + '<small>' + esc(s.purpose) + '</small></div><span class="pill w">' + (s.never ? 'never closes' : count(s.closes)) + '</span></div>').join('')
      : '<p class="lede">No BrowserAI window is on your screen. Hidden browsers are listed under Right now.</p>') + '</div>';
    html += '<div class="box"><h2>' + I.of('pulse') + 'Right now</h2>' + (D.conversations.length || D.sessions.length
      ? D.conversations.map((c) => '<div class="line">' + ava(c.app) + '<div class="txt">' + esc(c.title) + '<small>' + esc(placeLabel(c)) + '</small></div>' + (c.running ? '<span class="pill live">working</span>' : c.sessions.length ? (M.sessionById(c.sessions[0]).state === 'hidden' ? '<span class="pill h">hidden ' + count(M.sessionById(c.sessions[0]).closes) + '</span>' : M.sessionById(c.sessions[0]).state === 'window' ? '<span class="pill w">window</span>' : '<span class="pill">closed</span>') : '<span class="pill">no browser</span>') + '</div>').join('')
        + D.sessions.filter((s) => s.kept).map((s) => '<div class="line">' + I.of('folder') + '<div class="txt">' + esc(s.name) + '<small>kept, its conversation ended</small></div><span class="pill h">' + count(s.closes) + '</span></div>').join('')
      : '<p class="lede">Nothing uses BrowserAI right now.</p>') + '</div>';
    const u = D.update;
    html += '<div class="box"><h2>' + I.of('update') + 'Update</h2>' + (u.state === 'held'
      ? (u.wait === 'counting' ? '<div class="big">' + count(u.installsAt) + '</div><p class="lede">until ' + esc(u.version) + ' installs by itself, at about ' + M.clockOf(u.installsAt) + ', if nothing uses BrowserAI before then.</p>' : '<p class="lede">' + esc(u.version) + ' waits for a window that never closes by itself.</p>') + '<a class="btn" href="#/update">What holds it</a>'
      : '<p class="lede">' + esc(D.version) + ' is the newest. Checked ' + esc(u.source) + ' at ' + esc(u.checked) + '.</p>') + '</div>';
    html += '<div class="box"><div class="links">'
      + '<a href="#/sessions">' + I.of('globe') + '<span>Browser sessions</span><small>' + M.folderCount + '</small></a>'
      + '<a href="#/setup">' + I.of('plug') + '<span>Claude Code and Codex</span><small>' + (D.apps.every((a) => a.state === 'ok') ? 'both set up' : 'needs a look') + '</small></a>'
      + '<a href="#/storage">' + I.of('disk') + '<span>Storage</span><small>' + M.size(M.totalMb) + '</small></a>'
      + '<a href="#/folders">' + I.of('folder') + '<span>Folders and logs</span><small></small></a>'
      + '<a href="#/new">' + I.of('sparkle') + '<span>What is new in ' + esc(D.version) + '</span><small></small></a>'
      + '</div></div>';
    return html + '</aside>';
  }

  const back = '<a class="back" href="#/">' + I.of('back') + 'Back to today</a>';

  function sessions() {
    const open = D.sessions;
    let html = '<div class="page">' + back + '<h1>Browser sessions</h1><p class="lede">A session is a folder an agent made for its browser work: the profile with its logins and cookies, screenshots, downloads and a log. It stays until that agent deletes it.</p>';
    html += '<div class="sheet"><h3>Open now</h3>' + (open.map((s) => {
      const c = s.by ? M.conversationById(s.by) : null;
      return '<div class="rowx"><div><b>' + esc(s.name) + '</b><small>' + esc(s.purpose) + '. ' + (c ? 'Used by "' + esc(c.title) + '".' : 'Kept, its conversation ended.') + '</small></div>' + (s.state === 'window' ? '<span class="pill w">window, ' + (s.never ? 'never closes' : count(s.closes)) + '</span>' : s.state === 'hidden' ? '<span class="pill h">hidden, ' + count(s.closes) + '</span>' : '<span class="pill">closed</span>') + '<button class="btn" type="button">Open folder</button></div>';
    }).join('') || '<div class="rowx"><div><small>No session is open.</small></div></div>') + '</div>';
    html += '<div class="sheet"><h3>On disk</h3>' + (D.onDisk.map((s) => '<div class="rowx"><div><b>' + esc(s.name) + '</b>' + s.flags.map((f) => '<span class="flag">' + esc(f) + '</span>').join('') + '<small>' + esc(s.purpose) + ', last used ' + esc(s.used) + '</small></div><span class="pill">' + M.size(s.mb) + '</span><button class="btn" type="button">Open folder</button></div>').join('') || '<div class="rowx"><div><small>No other session folder.</small></div></div>') + '</div>';
    return html + '</div>';
  }

  function update() {
    const u = D.update;
    let html = '<div class="page">' + back;
    if (u.state !== 'held') return html + '<h1>BrowserAI is up to date</h1><p class="lede">' + esc(D.version) + ' is the newest. Checked ' + esc(u.source) + ' at ' + esc(u.checked) + '.</p></div>';
    html += '<h1>Update ' + esc(u.version) + '</h1><p class="lede">Downloaded at 13:36. It installs by itself once nothing below uses BrowserAI, so no agent is cut off.</p>';
    html += '<div class="sheet"><h3>Holding it</h3>'
      + M.windows.map((s) => '<div class="rowx"><div><b>' + esc(s.name) + '</b><small>A window on your screen. Close it to let the update go ahead.</small></div><span class="pill w">' + (s.never ? 'never' : count(s.closes)) + '</span><span></span></div>').join('')
      + M.hidden.map((s) => '<div class="rowx"><div><b>' + esc(s.name) + '</b><small>A hidden browser. It closes if no call uses it.</small></div><span class="pill h">' + count(s.closes) + '</span><span></span></div>').join('')
      + D.conversations.map((c) => '<div class="rowx"><div><b>' + esc(c.title) + '</b><small>' + esc(M.reconnectText[c.reconnect]) + '</small></div>' + (c.running ? '<span class="pill live">working</span>' : c.holdsUntil ? '<span class="pill">' + count(c.holdsUntil) + '</span>' : '<span class="pill">idle</span>') + '<span></span></div>').join('')
      + '</div><div class="sheet"><h3>Install now</h3><div class="rowx"><div><b>Install ' + esc(u.version) + ' now</b><small>Every browser closes cleanly, and every conversation loses BrowserAI for a moment. In a terminal, run /mcp, choose BrowserAI, then Reconnect. Codex needs a new conversation.</small></div><span></span><button class="btn bad" type="button">Install now</button></div></div>';
    return html + '</div>';
  }

  function setup() {
    let html = '<div class="page">' + back + '<h1>Claude Code and Codex</h1><p class="lede">Your agents reach BrowserAI through these apps. Each is set up on its own.</p>';
    for (const a of D.apps) {
      html += '<div class="sheet"><h3>' + esc(a.name) + '</h3><div class="rowx"><div><b>' + esc(a.short) + '</b><small>' + esc(a.sentence) + (a.restart ? ' ' + esc(a.restart) : '') + '</small></div><span></span>'
        + (a.state === 'missing' ? '<button class="btn" type="button" disabled>Install it first</button>' : a.state === 'repair' ? '<button class="btn dark" type="button">Repair</button>' : '<button class="btn" type="button">Set up again</button>') + '</div>'
        + (a.state !== 'missing' ? '<div class="rowx"><div><b>In one project</b><small>Writes or removes ' + esc(a.projectFile) + ' in a folder you choose, to commit with the project.</small></div><span></span><button class="btn" type="button">Choose a folder</button></div>' : '') + '</div>';
    }
    return html + '</div>';
  }

  function storage() {
    return '<div class="page">' + back + '<h1>' + M.size(M.totalMb) + ' on this PC</h1><p class="lede">' + plural(M.folderCount, 'session folder', 'session folders') + ' take ' + M.size(M.sessionsMb) + ', the downloaded browsers ' + M.size(M.browsersMb) + ' and the logs ' + M.size(Math.max(1, D.logsMb)) + '. A session folder stays until its agent deletes it.</p>'
      + '<div class="sheet"><h3>Largest session folders</h3>' + (D.onDisk.concat(D.sessions.map((s) => ({ name: s.name, mb: s.mb, used: s.lastAgo, flags: s.flags, purpose: s.purpose })))).sort((x, y) => y.mb - x.mb).slice(0, 6).map((s) => '<div class="rowx"><div><b>' + esc(s.name) + '</b><small>' + esc(s.purpose) + '</small></div><span class="pill">' + M.size(s.mb) + '</span><button class="btn" type="button">Open folder</button></div>').join('') + '</div></div>';
  }

  function folders() {
    let html = '<div class="page">' + back + '<h1>Folders and logs</h1><p class="lede">Where BrowserAI lives on this PC.</p>';
    if (D.broken) html += '<div class="box bad"><h2>' + I.of('bad') + 'BrowserAI needs reinstalling</h2><p class="lede">The browser server it starts for every session lists different tools from the ones this BrowserAI was built with; the first difference is ' + esc(D.broken.first) + '. Download BrowserAI.exe from the latest release and run it. It installs over this install and keeps every session.</p></div><br>';
    const row = (t, p) => '<div class="rowx"><div><b>' + t + '</b><small><code>' + esc(p) + '</code></small></div><span></span><button class="btn" type="button">Open</button></div>';
    return html + '<div class="sheet"><h3>On this PC</h3>' + row('Installed in', D.install.root) + row('Data', D.install.data) + row('Logs', D.install.logs) + '</div><div class="sheet"><h3>For troubleshooting</h3><div class="rowx"><div><b>What Claude Code and Codex start</b><small><code>' + esc(D.install.server) + '</code></small></div></div></div></div>';
  }

  function whatsNew() {
    const c = D.changelog;
    return '<div class="page">' + back + '<h1>What is new in ' + esc(c.version) + '</h1><p class="lede">Released ' + esc(c.date) + '.</p>' + c.groups.map((g) => '<div class="sheet"><h3>' + esc(g.name) + '</h3>' + g.entries.map((e) => '<div class="rowx"><div><b>' + e.icon + ' ' + esc(e.head) + '</b><small>' + esc(e.body) + '</small></div></div>').join('') + '</div>').join('') + '</div>';
  }

  const routes = { '': feed, sessions, update, setup, storage, folders, new: whatsNew };
  const navs = [['', 'Today', 'clock'], ['sessions', 'Sessions', 'globe'], ['setup', 'Claude Code and Codex', 'plug'], ['storage', 'Storage', 'disk']];

  function render() {
    const name = location.hash.replace(/^#\/?/, '').split('/')[0];
    root.innerHTML = (routes[name] || feed)();
    for (const a of document.querySelectorAll('.mast nav a')) {
      if (a.dataset.key === (routes[name] ? name : '')) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    }
    M.tick(root);
    window.scrollTo(0, 0);
  }

  document.getElementById('mast').innerHTML = '<div class="mast-in"><a class="logo" href="#/">' + I.brand('m') + '<span class="word">BrowserAI</span></a><nav aria-label="Views">'
    + navs.map((n) => '<a href="#/' + n[0] + '" data-key="' + n[0] + '"><span>' + esc(n[1]) + '</span></a>').join('') + '</nav><span class="gap"></span><span class="ver">' + esc(D.version) + ', running since ' + esc(D.since) + '</span></div>';

  document.addEventListener('click', (e) => {
    const f = e.target.closest('[data-f]');
    if (!f) return;
    for (const b of document.querySelectorAll('[data-f]')) b.setAttribute('aria-pressed', String(b === f));
    for (const ev of document.querySelectorAll('.ev')) {
      ev.classList.toggle('hidden-by-filter', f.dataset.f !== '' && !ev.dataset.groups.split(' ').includes(f.dataset.f));
    }
  });
  window.addEventListener('hashchange', render);
  render();
  M.startTicking();
  M.mockControl();
})();

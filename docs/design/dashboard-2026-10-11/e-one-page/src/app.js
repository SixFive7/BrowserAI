// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

(() => {
  const M = MOCK;
  const D = M.data;
  const I = ICON;
  const esc = M.esc;
  M.applyTheme();

  const count = (ms) => '<span class="count" data-ends="' + ms + '"></span>';
  const plural = (n, one, many) => n + ' ' + (n === 1 ? one : many);
  const ava = (app) => '<span class="ava ' + (app === 'Codex' ? 'cx' : 'cc') + '" aria-hidden="true">' + (app === 'Codex' ? 'CX' : 'CC') + '</span>';
  const placeLabel = (c) => c.place === 'VS Code' ? 'Claude Code in VS Code' : c.place === 'Terminal' ? 'Claude Code in a terminal' : 'Codex';
  const chip = (s) => s.state === 'window' ? '<span class="chip w">' + I.of('window') + (s.never ? 'Window, never closes' : 'Window on your screen, ' + count(s.closes)) + '</span>'
    : s.state === 'hidden' ? '<span class="chip h">' + I.of('hidden') + 'Hidden, closes in ' + count(s.closes) + '</span>'
    : '<span class="chip">' + I.of('closed') + 'Closed, folder kept</span>';

  // ---- The sentence that answers the first question --------------------------------

  function sentence() {
    if (D.broken) return 'BrowserAI is running, but <em class="bad">it needs reinstalling</em>: every session is refused until it is.';
    if (D.occasion === 'firstrun') return 'BrowserAI is installed and running. <em>Claude Code can use it now</em>; Codex was not found on this PC.';
    const n = D.conversations.length;
    if (n === 0) return 'BrowserAI is running and ready. <em>Nothing is using it</em> right now.';
    const w = M.windows.length;
    return 'BrowserAI is running. <em>' + plural(n, 'conversation is', 'conversations are') + ' using it</em>' + (w ? ', and ' + (w === 1 ? 'one browser window is' : w + ' browser windows are') + ' open on your screen.' : ', all in hidden browsers.');
  }

  function facts() {
    const u = D.update;
    const okApps = D.apps.every((a) => a.state === 'ok');
    return '<div class="facts">'
      + (u.state === 'held' ? '<a class="fact warn" href="#update" data-go="update">' + I.of('update') + (u.wait === 'counting' ? 'Update ' + esc(u.version) + ' installs at about ' + M.clockOf(u.installsAt) : 'Update ' + esc(u.version) + ' waits for a window') + '</a>' : '<a class="fact" href="#update" data-go="update">' + I.of('checkCircle') + 'Up to date</a>')
      + '<a class="fact' + (okApps ? '' : ' warn') + '" href="#apps" data-go="apps">' + I.of('plug') + (okApps ? 'Claude Code and Codex set up' : D.apps.filter((a) => a.state !== 'ok').map((a) => esc(a.name) + ': ' + esc(a.short.toLowerCase())).join(', ')) + '</a>'
      + '<a class="fact" href="#storage" data-go="storage">' + I.of('disk') + M.size(M.totalMb) + ' on this PC</a>'
      + '</div>';
  }

  // ---- The panels ------------------------------------------------------------------------

  function panel(id, icon, tone, title, summary, body, open) {
    return '<section class="panel" id="' + id + '" data-open="' + (open ? 'true' : 'false') + '"><button class="panel-head" type="button" aria-expanded="' + (open ? 'true' : 'false') + '" aria-controls="' + id + '-body"><span class="badge-ic ' + tone + '">' + I.of(icon) + '</span><h2>' + esc(title) + '</h2><span class="sum">' + summary + '</span>' + I.of('chevronDown', 'chev') + '</button><div class="panel-body" id="' + id + '-body">' + body + '</div></section>';
  }

  function nowPanel() {
    const n = D.conversations.length;
    let body = '<div class="what"><div><b>Conversation</b>A chat in Claude Code or Codex, connected while it is open.</div><div><b>Session</b>A folder an agent made for its browser: profile, screenshots, downloads, log.</div><div><b>Browser</b>Hidden, or a window on your screen. It closes when nobody uses it.</div></div>';
    if (n === 0 && D.sessions.length === 0) {
      body += '<div class="empty"><b>No conversation is using BrowserAI</b>When an agent in Claude Code or Codex calls a BrowserAI tool, its conversation appears here, with the browser it opened.</div>';
    } else {
      body += '<div class="list">' + D.conversations.map((c) => {
        const ss = c.sessions.map((id) => M.sessionById(id));
        return '<div class="li">' + ava(c.app) + '<div class="txt"><b>' + esc(c.title) + '</b><small>' + esc(placeLabel(c)) + ', ' + esc(c.folderName) + '</small></div>' + (c.running ? '<span class="chip live"><span class="pulse"></span>Working now</span>' : '') + (ss.length ? '' : '<span class="chip">No browser yet</span>') + '</div>'
          + ss.map((s) => '<div class="li sub' + (s.state === 'window' ? ' window' : '') + '"><span class="ava kept">' + I.of('folder') + '</span><div class="txt"><b>' + esc(s.name) + '</b><small>' + esc(s.browser) + ', ' + esc(s.purpose) + '</small></div>' + chip(s) + '<button class="btn quiet" type="button">Open folder</button></div>').join('');
      }).join('')
        + D.sessions.filter((s) => s.kept).map((s) => '<div class="li"><span class="ava kept">' + I.of('folder') + '</span><div class="txt"><b>' + esc(s.name) + '</b><small>Kept after its conversation ended, for the next one that names it</small></div>' + chip(s) + '<button class="btn quiet" type="button">Open folder</button></div>').join('')
        + '</div><p class="explain">Nothing here can be closed from this page. An agent closes its own browser, and a conversation ends when its app closes it.</p>';
    }
    const sum = n ? '<b>' + plural(n, 'conversation', 'conversations') + '</b>, ' + plural(M.openBrowsers, 'browser', 'browsers') + (M.windows.length ? ', <b>' + M.windows.length + ' on your screen</b>' : '') : 'Nothing right now';
    return panel('now', 'pulse', n ? '' : 'gray', 'What is running now', sum, body, true);
  }

  function updatePanel() {
    const u = D.update;
    if (u.state !== 'held') {
      return panel('update', 'update', 'gray', 'Update', 'Up to date, checked ' + esc(u.checked), '<p class="explain">' + esc(D.version) + ' is the newest version. BrowserAI checked ' + esc(u.source) + ' at ' + esc(u.checked) + '. When a newer one is out, BrowserAI downloads it and installs it once nothing uses BrowserAI, and a notification says so.</p>', false);
    }
    const holders = M.windows.map((s) => '<div class="li window">' + I.of('window') + '<div class="txt"><b>' + esc(s.name) + '</b><small>A window on your screen. Close it to let the update go ahead.</small></div><span class="chip w">' + (s.never ? 'never closes' : count(s.closes)) + '</span></div>').join('')
      + M.hidden.map((s) => '<div class="li">' + I.of('hidden') + '<div class="txt"><b>' + esc(s.name) + '</b><small>' + (s.kept ? 'Hidden browser, kept after its conversation ended' : 'Hidden browser, closes if no call uses it') + '</small></div><span class="chip h">' + count(s.closes) + '</span></div>').join('')
      + D.conversations.filter((c) => c.running || c.holdsUntil).map((c) => '<div class="li">' + I.of(c.place === 'Terminal' ? 'terminal' : 'code') + '<div class="txt"><b>' + esc(c.title) + '</b><small>' + esc(M.reconnectText[c.reconnect]) + '</small></div>' + (c.running ? '<span class="chip live">working</span>' : '<span class="chip">' + count(c.holdsUntil) + '</span>') + '</div>').join('');
    const body = (u.wait === 'counting' ? '<div class="countdown"><span class="num">' + count(u.installsAt) + '</span><span>until ' + esc(u.version) + ' installs by itself, at about ' + M.clockOf(u.installsAt) + ', if nothing below is used before then.</span></div>' : '<div class="notice warn">' + I.of('window') + '<p><b>A window keeps it waiting.</b> It is set never to close by itself. Close it, and the update installs once nothing else uses BrowserAI.</p></div>')
      + '<p class="mini-h">Still using BrowserAI</p><div class="list">' + holders + '</div>'
      + '<div class="btns"><button class="btn danger" type="button">Install ' + esc(u.version) + ' now</button></div><p class="explain">Installing now closes every browser, windows included, and ends every connection. Claude Code in VS Code reconnects by itself; in a terminal, run /mcp, choose BrowserAI, then Reconnect; a Codex conversation needs a new conversation. BrowserAI starts again by itself.</p>';
    return panel('update', 'update', 'warn', 'Update ' + u.version + ' is ready', u.wait === 'counting' ? 'Installs in <b>' + count(u.installsAt) + '</b>' : '<b>Waits for a window</b>', body, true);
  }

  function appsPanel() {
    const bad = D.apps.some((a) => a.state === 'repair');
    const body = '<p class="explain">Your agents reach BrowserAI through these apps, and each one is set up on its own: for all your projects, or in one project folder.</p><div class="list">'
      + D.apps.map((a) => '<div class="li">' + ava(a.name) + '<div class="txt"><b>' + esc(a.name) + ': ' + esc(a.short.toLowerCase()) + '</b><small>' + esc(a.sentence) + '</small></div>'
        + (a.state === 'missing' ? '<button class="btn" type="button" disabled>Install it first</button>' : a.state === 'repair' ? '<button class="btn primary" type="button">Repair</button>' : '<button class="btn" type="button">Set up again</button><button class="btn quiet" type="button">In one project</button>') + '</div>').join('')
      + '</div>';
    const sum = D.apps.every((a) => a.state === 'ok') ? 'Both set up for all your projects' : D.apps.map((a) => esc(a.name) + ': <b>' + esc(a.short.toLowerCase()) + '</b>').join(', ');
    return panel('apps', 'plug', bad ? 'warn' : '', 'Claude Code and Codex', sum, body, bad || D.occasion === 'firstrun');
  }

  function storagePanel() {
    const total = M.totalMb;
    const a = (M.sessionsMb / total) * 100;
    const b = (M.browsersMb / total) * 100;
    const top = D.onDisk.concat(D.sessions.map((s) => ({ name: s.name, mb: s.mb, used: s.running ? 'now' : s.lastAgo, flags: s.flags }))).sort((x, y) => y.mb - x.mb).slice(0, 4);
    const body = '<svg class="bar" viewBox="0 0 100 10" preserveAspectRatio="none" aria-hidden="true"><rect class="seg-a" x="0" y="0" width="' + a + '" height="10"/><rect class="seg-b" x="' + a + '" y="0" width="' + b + '" height="10"/><rect class="seg-c" x="' + (a + b) + '" y="0" width="' + (100 - a - b) + '" height="10"/></svg>'
      + '<p class="explain">' + plural(M.folderCount, 'session folder', 'session folders') + ' take ' + M.size(M.sessionsMb) + ', the downloaded browsers ' + M.size(M.browsersMb) + ' and the logs ' + M.size(Math.max(1, D.logsMb)) + '. A session folder stays until the agent that made it deletes it.</p>'
      + (top.length ? '<p class="mini-h">Largest session folders</p><div class="list">' + top.map((s) => '<div class="li">' + I.of('folder') + '<div class="txt"><b>' + esc(s.name) + s.flags.map((f) => '<span class="flag">' + esc(f) + '</span>').join('') + '</b><small>last used ' + esc(s.used) + '</small></div><span class="chip">' + M.size(s.mb) + '</span><button class="btn quiet" type="button">Open folder</button></div>').join('') + '</div>' : '<div class="empty"><b>No session folder yet</b>Each session an agent starts is a folder it names; they appear here with their size.</div>');
    return panel('storage', 'disk', 'gray', 'Storage', M.size(total) + ' on this PC', body, false);
  }

  function filesPanel() {
    const row = (t, p) => '<div class="li">' + I.of('folder') + '<div class="txt"><b>' + t + '</b><small><code>' + esc(p) + '</code></small></div><button class="btn" type="button">Open</button></div>';
    const body = (D.broken ? '<div class="notice bad">' + I.of('bad') + '<p><b>BrowserAI needs reinstalling.</b> The browser server it starts for every session lists different tools from the ones this BrowserAI was built with; the first difference is ' + esc(D.broken.first) + '. Download BrowserAI.exe from the latest release and run it. It installs over this install and keeps every session.</p></div>' : '')
      + '<div class="list">' + row('Installed in', D.install.root) + row('Data: browsers, the session index and logs', D.install.data) + row('Logs', D.install.logs) + '</div><p class="explain">Claude Code and Codex start <code>' + esc(D.install.server) + '</code>. Started with no argument, the same file opens this page.</p>';
    return panel('files', D.broken ? 'bad' : 'folder', D.broken ? 'bad' : 'gray', D.broken ? 'Install: needs reinstalling' : 'Folders and logs', 'Running since ' + esc(D.since), body, Boolean(D.broken));
  }

  function newPanel() {
    const c = D.changelog;
    const body = '<div class="cols">' + c.groups.map((g) => '<div><p class="mini-h">' + esc(g.name) + '</p>' + g.entries.map((e) => '<div class="entry">' + e.icon + ' ' + esc(e.head) + '<small>' + esc(e.body) + '</small></div>').join('') + '</div>').join('') + '</div>';
    return panel('new', 'sparkle', 'gray', 'What is new in ' + c.version, 'Released ' + esc(c.date), body, false);
  }

  function render() {
    const attention = M.attention.map((a) => '<div class="notice ' + a.level + '" role="alert">' + I.of(a.level === 'bad' ? 'bad' : 'warn') + '<p><b>' + esc(a.title) + '.</b> ' + esc(a.text) + '</p></div>').join('');
    document.getElementById('hero').innerHTML = '<div class="hero-in"><div class="hero-top">' + I.brand('e') + 'BrowserAI <span class="ver">' + esc(D.version) + '</span><span class="gap"></span><a href="#" title="Opens the README on GitHub">' + I.of('question') + 'How BrowserAI works</a></div><h1>' + sentence() + '</h1>' + facts() + '</div>';
    document.getElementById('jump').innerHTML = '<div class="jump-in"><a href="#now" data-go="now">Now</a><a href="#update" data-go="update">Update</a><a href="#apps" data-go="apps">Claude Code and Codex</a><a href="#storage" data-go="storage">Storage</a><a href="#files" data-go="files">Folders and logs</a><a href="#new" data-go="new">What is new</a></div>';
    document.getElementById('main').innerHTML = attention + nowPanel() + updatePanel() + appsPanel() + storagePanel() + filesPanel() + newPanel();
    const params = new URLSearchParams(location.search);
    if (params.get('open') === 'all') for (const p of document.querySelectorAll('.panel')) setOpen(p, true);
    const target = location.hash.slice(1);
    if (target && document.getElementById(target)) { setOpen(document.getElementById(target), true); }
    M.tick();
  }

  function setOpen(p, open) {
    p.dataset.open = String(open);
    p.querySelector('.panel-head').setAttribute('aria-expanded', String(open));
  }

  document.addEventListener('click', (e) => {
    const head = e.target.closest('.panel-head');
    if (head) { const p = head.closest('.panel'); setOpen(p, p.dataset.open !== 'true'); return; }
    const go = e.target.closest('[data-go]');
    if (go) {
      e.preventDefault();
      const p = document.getElementById(go.dataset.go);
      setOpen(p, true);
      p.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'start' });
      history.replaceState(null, '', location.search + '#' + go.dataset.go);
    }
  });

  render();
  M.startTicking();
  M.mockControl();
})();

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
  const chev = I.of('chevron', 'chev');
  const pages = [
    { key: '', label: 'Home', icon: 'home' },
    { key: 'conversations', label: 'Conversations', icon: 'chat', badge: () => D.conversations.length ? String(D.conversations.length) : '', quiet: true },
    { key: 'sessions', label: 'Browsers and sessions', icon: 'globe', badge: () => M.openBrowsers ? M.openBrowsers + ' open' : '', quiet: true },
    { key: 'update', label: 'Update', icon: 'update', badge: () => D.update.state === 'held' ? '1' : '' },
    { key: 'apps', label: 'Claude Code and Codex', icon: 'plug', badge: () => D.apps.some((a) => a.state === 'repair') ? '!' : '', warn: true },
    { key: 'storage', label: 'Storage', icon: 'disk' },
    { key: 'files', label: 'Folders and logs', icon: 'folder' },
    { key: 'new', label: 'What is new', icon: 'sparkle' },
  ];

  function browserPill(s) {
    if (!s) return '';
    if (s.state === 'window') return '<span class="pill window">' + I.of('window') + 'Window on your screen</span>';
    if (s.state === 'hidden') return '<span class="pill hidden">' + I.of('hidden') + 'Hidden, closes in ' + count(s.closes) + '</span>';
    return '<span class="pill">' + I.of('closed') + 'Closed</span>';
  }

  function where(c) {
    return c.place === 'VS Code' ? 'Claude Code in VS Code' : c.place === 'Terminal' ? 'Claude Code in a terminal' : 'Codex';
  }

  function notices() {
    let html = '';
    for (const a of M.attention) {
      html += '<div class="infobar ' + a.level + '" role="alert">' + I.of(a.level === 'bad' ? 'bad' : 'warn') + '<div class="txt"><b>' + esc(a.title) + '</b>' + esc(a.text) + '</div>'
        + (a.kind === 'broken' ? '<a class="btn" href="#/files">How to reinstall</a>' : a.kind === 'app' ? '<a class="btn" href="#/apps">Repair</a>' : '<a class="btn" href="#/update">Open Update</a>') + '</div>';
    }
    if (D.occasion === 'firstrun') {
      html += '<div class="infobar ok">' + I.of('checkCircle') + '<div class="txt"><b>BrowserAI is installed</b>It runs in the background from now on. Claude Code is set up for all your projects. Claude Code sessions that were already open see BrowserAI after a restart.</div><a class="btn" href="#/apps">See the setup</a></div>';
    }
    return html;
  }

  // ---- Home ------------------------------------------------------------------

  function home() {
    const u = D.update;
    const okApps = D.apps.filter((a) => a.state === 'ok').length;
    const status = M.attention.length ? plural(M.attention.length, 'thing needs you', 'things need you') : 'Running';
    let html = '<h1 class="page-title">Home</h1><p class="page-sub">BrowserAI gives your agents in Claude Code and Codex a real browser. It runs in the background from sign-in to sign-out.</p>' + notices();
    html += '<div class="hero">'
      + '<a class="tile first" href="#/conversations"><div class="tile-head">' + I.of('pulse') + 'BrowserAI</div><div class="tile-value">' + esc(status) + '</div><div class="tile-note">' + (D.conversations.length ? plural(D.conversations.length, 'conversation', 'conversations') + ' and ' + plural(M.openBrowsers, 'browser', 'browsers') + ' right now. ' : 'Nothing uses it right now. ') + 'Version ' + esc(D.version) + ', running since ' + esc(D.since) + '.</div></a>'
      + '<a class="tile" href="#/update"><div class="tile-head">' + I.of('update') + 'Update</div>' + (u.state === 'held'
        ? '<div class="tile-value">' + esc(u.version) + ' is ready</div><div class="tile-note">' + (u.wait === 'counting' ? 'Installs by itself in ' + count(u.installsAt) + ', once nothing uses BrowserAI.' : 'Waits for a window that never closes by itself.') + '</div>'
        : '<div class="tile-value">Up to date</div><div class="tile-note">Checked ' + esc(u.source) + ' at ' + esc(u.checked) + '.</div>') + '</a>'
      + '<a class="tile" href="#/apps"><div class="tile-head">' + I.of('plug') + 'Claude Code and Codex</div><div class="tile-value">' + (okApps === D.apps.length ? 'Both set up' : okApps + ' of ' + D.apps.length + ' set up') + '</div><div class="tile-note">' + (okApps === D.apps.length ? 'Agents in both apps can use BrowserAI in every project.' : D.apps.map((a) => esc(a.name) + ': ' + esc(a.short.toLowerCase())).join('. ') + '.') + '</div></a>'
      + '</div>';

    html += '<div class="group-title">Right now <span>Who uses BrowserAI, and the browsers they opened</span></div>';
    if (D.conversations.length === 0 && D.sessions.length === 0) {
      html += '<div class="empty">' + I.of('chat') + '<div><b>No conversation is using BrowserAI</b><span>When an agent in Claude Code or Codex calls a BrowserAI tool, its conversation appears here, with the browser it opened.</span></div></div>';
    } else {
      html += '<div class="rows">' + D.conversations.map((c) => {
        const s = c.sessions.length ? M.sessionById(c.sessions[0]) : null;
        const right = (c.running ? '<span class="pill live"><span class="pulse"></span>Working</span>' : '') + (s ? browserPill(s) : '<span class="pill">No browser yet</span>');
        return '<a class="srow" href="#/conversations">' + ava(c.app) + '<span class="txt"><span class="t">' + esc(c.title) + '</span><span class="d">' + esc(where(c)) + ', ' + esc(c.folderName) + (s ? ', session ' + esc(s.name) : '') + '</span></span><span class="side-val">' + right + '</span>' + chev + '</a>';
      }).join('')
        + D.sessions.filter((s) => s.kept).map((s) => '<a class="srow" href="#/sessions/' + s.id + '"><span class="ava kept">' + I.of('folder') + '</span><span class="txt"><span class="t">' + esc(s.name) + '</span><span class="d">Kept after its conversation ended, for the next one that names it</span></span><span class="side-val">' + browserPill(s) + '</span>' + chev + '</a>').join('')
        + '</div>';
    }

    html += '<div class="group-title">More <span>Everything else BrowserAI keeps track of</span></div><div class="rows">'
      + '<a class="srow" href="#/storage">' + I.of('disk') + '<span class="txt"><span class="t">Storage</span><span class="d">Session folders, downloaded browsers and logs</span></span><span class="side-val">' + M.size(M.totalMb) + '</span>' + chev + '</a>'
      + '<a class="srow" href="#/files">' + I.of('folder') + '<span class="txt"><span class="t">Folders and logs</span><span class="d">Where BrowserAI is installed, where it keeps its data, and its log</span></span>' + chev + '</a>'
      + '<a class="srow" href="#/new">' + I.of('sparkle') + '<span class="txt"><span class="t">What is new in ' + esc(D.version) + '</span><span class="d">The changes in the version you have</span></span>' + chev + '</a>'
      + '</div>';
    return html;
  }

  // ---- Conversations -----------------------------------------------------------

  function conversations() {
    let html = '<h1 class="page-title">Conversations</h1><p class="page-sub">A conversation is a chat in Claude Code or Codex. It is connected to BrowserAI while it is open, and BrowserAI cannot end it from here: it ends when its app closes it.</p>';
    if (D.conversations.length === 0) {
      return html + '<div class="empty">' + I.of('chat') + '<div><b>No conversation is connected</b><span>Start a conversation in Claude Code or Codex and ask it to use a browser. It appears here once it calls a BrowserAI tool.</span></div></div>';
    }
    const groups = [];
    const seen = new Set();
    for (const c of D.conversations) {
      if (c.windowKey) {
        if (!seen.has(c.windowKey)) {
          seen.add(c.windowKey);
          groups.push({ title: c.windowLabel, hint: 'The tabs of one VS Code window', items: D.conversations.filter((x) => x.windowKey === c.windowKey) });
        }
      } else {
        groups.push({ title: c.place === 'Terminal' ? 'Terminal' : 'Codex', hint: c.place === 'Terminal' ? 'Claude Code in a terminal window' : 'Codex app', items: [c] });
      }
    }
    for (const g of groups) {
      html += '<div class="group-title">' + esc(g.title) + ' <span>' + esc(g.hint) + '</span></div>';
      for (const c of g.items) {
        const lines = c.sessions.map((id) => {
          const s = M.sessionById(id);
          return '<div class="exp-line">' + I.of(s.state === 'window' ? 'window' : s.state === 'hidden' ? 'hidden' : 'closed') + '<span class="txt"><span class="t">' + esc(s.name) + '</span><span class="d">' + esc(s.purpose) + '</span></span>' + browserPill(s) + '<a class="btn" href="#/sessions/' + s.id + '">Open session</a></div>';
        }).join('') || '<div class="exp-line"><span class="txt"><span class="d">No browser yet. This conversation is connected and has not opened a session.</span></span></div>';
        html += '<details class="exp" open><summary>' + ava(c.app) + '<span class="txt"><span class="t">' + esc(c.title) + '</span><span class="d">' + esc(c.app) + ' ' + esc(c.version) + ' in <code>' + esc(c.folder) + '</code>, connected since ' + esc(c.since) + '</span></span><span class="side-val">' + (c.running ? '<span class="pill live"><span class="pulse"></span>Working now</span>' : c.holdsUntil ? '<span class="pill">Called recently</span>' : '<span class="pill">Idle</span>') + '</span>' + chev + '</summary><div class="exp-body">'
          + lines + '<div class="exp-line">' + I.of('info') + '<span class="txt"><span class="d">' + esc(M.reconnectText[c.reconnect]) + '</span></span></div></div></details>';
      }
    }
    return html;
  }

  // ---- Browsers and sessions -----------------------------------------------------

  function sessions(id) {
    if (id) return sessionPage(id);
    let html = '<h1 class="page-title">Browsers and sessions</h1><p class="page-sub">A session is a folder an agent made for its browser work: the browser profile with its logins and cookies, screenshots, downloads and a log. While its browser runs, it is hidden or a window on your screen, and it closes by itself when nobody uses it.</p>';
    html += '<div class="group-title">Open now <span>' + plural(M.openBrowsers, 'browser', 'browsers') + ' running</span></div>';
    const open = D.sessions;
    if (open.length === 0) {
      html += '<div class="empty">' + I.of('globe') + '<div><b>No browser is open</b><span>A browser opens when an agent starts or resumes a session, and closes after its idle minutes.</span></div></div>';
    } else {
      html += '<div class="rows">' + open.map((s) => {
        const c = s.by ? M.conversationById(s.by) : null;
        return '<a class="srow" href="#/sessions/' + s.id + '">' + I.of(s.state === 'window' ? 'window' : s.state === 'hidden' ? 'hidden' : 'closed') + '<span class="txt"><span class="t">' + esc(s.name) + '</span><span class="d">' + esc(s.browser) + ', ' + (c ? 'used by "' + esc(c.title) + '"' : 'kept, no conversation') + '</span></span><span class="side-val">' + browserPill(s) + '</span>' + chev + '</a>';
      }).join('') + '</div>';
    }
    html += '<div class="group-title">On disk <span>' + plural(D.onDisk.length, 'other session folder', 'other session folders') + ', kept until their agent deletes them</span></div>';
    html += D.onDisk.length ? '<div class="rows">' + D.onDisk.map((s) => '<div class="srow">' + I.of('folder') + '<span class="txt"><span class="t">' + esc(s.name) + s.flags.map((f) => '<span class="flag">' + esc(f) + '</span>').join('') + '</span><span class="d">' + esc(s.purpose) + ', last used ' + esc(s.used) + '</span></span><span class="side-val">' + M.size(s.mb) + '<button class="btn" type="button">' + I.of('folder') + 'Open folder</button></span></div>').join('') + '</div>'
      : '<div class="empty">' + I.of('folder') + '<div><b>No other session folder</b><span>Session folders an agent made and left are listed here, with their size.</span></div></div>';
    return html;
  }

  function sessionPage(id) {
    const s = M.sessionById(id) || D.sessions[0];
    if (!s) return sessions();
    const c = s.by ? M.conversationById(s.by) : null;
    let html = '<div class="crumb"><a href="#/sessions">Browsers and sessions</a>' + I.of('chevron') + esc(s.name) + '</div><p class="page-sub">Its agent wrote that it is for ' + esc(s.purpose) + '.</p>';
    html += '<div class="rows">'
      + '<div class="srow">' + I.of(s.state === 'window' ? 'window' : s.state === 'hidden' ? 'hidden' : 'closed') + '<span class="txt"><span class="t">Browser</span><span class="d">' + esc(s.browser) + (s.state === 'window' ? ', a window on your screen. Closes after an hour nobody uses it.' : s.state === 'hidden' ? ', hidden. Closes after its idle minutes.' : ', closed. A call that names the session opens it again.') + '</span></span><span class="side-val">' + browserPill(s) + '</span></div>'
      + '<div class="srow">' + I.of('chat') + '<span class="txt"><span class="t">Used by</span><span class="d">' + (c ? esc(c.title) + ', ' + esc(where(c)) : 'No conversation. BrowserAI keeps the session for the next one that names it.') + '</span></span></div>'
      + '<div class="srow">' + I.of('pulse') + '<span class="txt"><span class="t">Latest call</span><span class="d">' + esc(s.lastWhy) + ' (' + (s.running ? 'running now' : esc(s.lastAgo)) + ')</span></span></div>'
      + '<div class="srow">' + I.of('folder') + '<span class="txt"><span class="t">Folder</span><span class="d"><code>' + esc(s.folder) + '</code></span></span><span class="side-val">' + M.size(s.mb) + '<button class="btn" type="button">Open folder</button></span></div>'
      + '</div>';
    html += '<div class="group-title">What the folder holds <span>Files that can carry a login or a password</span></div><div class="rows">'
      + '<div class="srow">' + I.of('globe') + '<span class="txt"><span class="t">Browser profile</span><span class="d">Logins and cookies of every site it visited, until the folder is deleted</span></span><span class="side-val">Always</span></div>'
      + (s.flags.includes('transcript') ? '<div class="srow">' + I.of('file') + '<span class="txt"><span class="t">Transcript</span><span class="d">Every call and its result, and what was typed into pages, passwords included</span></span><span class="side-val">session.md</span></div>' : '')
      + (s.flags.includes('saved login') ? '<div class="srow">' + I.of('key') + '<span class="txt"><span class="t">Saved login</span><span class="d">The cookies and storage of a signed-in site, in one file</span></span></div>' : '')
      + (s.traces ? '<div class="srow">' + I.of('list') + '<span class="txt"><span class="t">' + plural(s.traces, 'Trace', 'Traces') + '</span><span class="d">A recording Playwright\'s trace viewer can replay</span></span></div>' : '')
      + '</div><div class="infobar gap-top">' + I.of('info') + '<div class="txt">Only the agent that made a session deletes it. BrowserAI never deletes a session folder on its own.</div></div>';
    return html;
  }

  // ---- Update -------------------------------------------------------------------

  function update() {
    const u = D.update;
    let html = '<h1 class="page-title">Update</h1>';
    if (u.state !== 'held') {
      return html + '<p class="page-sub">BrowserAI checks for a newer version by itself and installs it once nothing uses BrowserAI. A notification tells you before and after.</p><div class="rows"><div class="srow">' + I.of('checkCircle') + '<span class="txt"><span class="t">You have the newest version</span><span class="d">' + esc(D.version) + '. Checked ' + esc(u.source) + ' at ' + esc(u.checked) + '.</span></span></div><div class="srow">' + I.of('globe') + '<span class="txt"><span class="t">Updates come from</span><span class="d">' + esc(u.source) + '</span></span></div></div>';
    }
    html += '<p class="page-sub">' + esc(u.version) + ' is downloaded. It installs by itself once nothing uses BrowserAI, so no agent is cut off in the middle of its work.</p>';
    html += '<div class="rows"><div class="srow">' + I.of('clock') + '<span class="txt"><span class="t">' + (u.wait === 'counting' ? 'Installs by itself at about ' + M.clockOf(u.installsAt) : 'Waits for a window') + '</span><span class="d">' + (u.wait === 'counting' ? 'If nothing below is used before then. Each countdown starts again when it is used.' : 'A window set never to close by itself holds it. It installs once you close that window.') + '</span></span><span class="side-val">' + (u.wait === 'counting' ? '<span class="big-count">' + count(u.installsAt) + '</span>' : '') + '<button class="btn bad" type="button">Install now</button></span></div></div>';
    const line = (icon, t, d, side) => '<div class="exp-line">' + I.of(icon) + '<span class="txt"><span class="t">' + t + '</span><span class="d">' + d + '</span></span>' + side + '</div>';
    html += '<div class="group-title">What holds it <span>Each item lets the update go when its countdown ends</span></div>';
    html += '<details class="exp" open><summary>' + I.of('window') + '<span class="txt"><span class="t">Windows on your screen</span><span class="d">Close one to let the update go ahead. Each closes after an hour nobody uses it.</span></span><span class="side-val">' + M.windows.length + '</span>' + chev + '</summary><div class="exp-body">'
      + (M.windows.map((s) => line('window', esc(s.name), esc(s.purpose), s.never ? '<span class="pill window">Never closes</span>' : '<span class="pill window">' + count(s.closes) + '</span>')).join('') || line('check', 'None', 'No window is open.', '')) + '</div></details>';
    html += '<details class="exp" open><summary>' + I.of('hidden') + '<span class="txt"><span class="t">Hidden browsers</span><span class="d">Each closes by itself after its idle minutes.</span></span><span class="side-val">' + M.hidden.length + '</span>' + chev + '</summary><div class="exp-body">'
      + (M.hidden.map((s) => line('hidden', esc(s.name), s.kept ? 'Kept after its conversation ended' : esc(s.purpose), '<span class="pill hidden">' + count(s.closes) + '</span>')).join('') || line('check', 'None', 'No hidden browser is open.', '')) + '</div></details>';
    html += '<details class="exp" open><summary>' + I.of('chat') + '<span class="txt"><span class="t">Conversations</span><span class="d">Each holds the update for ten minutes after its last call, and loses BrowserAI for a moment when it installs.</span></span><span class="side-val">' + D.conversations.length + '</span>' + chev + '</summary><div class="exp-body">'
      + (D.conversations.map((c) => line(c.place === 'Terminal' ? 'terminal' : c.place === 'VS Code' ? 'code' : 'chat', esc(c.title), esc(M.reconnectText[c.reconnect]), c.running ? '<span class="pill live"><span class="pulse"></span>Working</span>' : c.holdsUntil ? '<span class="pill">' + count(c.holdsUntil) + '</span>' : '<span class="pill">Idle</span>')).join('') || line('check', 'None', 'No conversation is connected.', '')) + '</div></details>';
    html += '<div class="group-title">Install now <span>Does not wait for any of the above</span></div><div class="rows"><div class="srow">' + I.of('bolt') + '<span class="txt"><span class="t">Install ' + esc(u.version) + ' now</span><span class="d">Every browser closes cleanly, windows included. Claude Code in VS Code reconnects by itself; in a terminal, run /mcp, choose BrowserAI, then Reconnect; a Codex conversation needs a new conversation. BrowserAI starts again by itself.</span></span><button class="btn bad" type="button">Install now</button></div></div>';
    return html;
  }

  // ---- Claude Code and Codex ------------------------------------------------------

  function apps() {
    let html = '<h1 class="page-title">Claude Code and Codex</h1><p class="page-sub">Your agents reach BrowserAI through these apps, and each one is set up on its own. All your projects means one entry in your own settings of that app; one project means a file in that project\'s folder, to commit with it.</p>';
    for (const a of D.apps) {
      const pill = a.state === 'ok' ? '<span class="pill ok">' + I.of('check') + 'Set up</span>' : a.state === 'repair' ? '<span class="pill bad">' + I.of('warn') + 'Needs repair</span>' : '<span class="pill">Not found</span>';
      html += '<div class="group-title">' + esc(a.name) + '</div><details class="exp" open><summary>' + ava(a.name) + '<span class="txt"><span class="t">' + esc(a.short) + '</span><span class="d">' + esc(a.sentence) + '</span></span><span class="side-val">' + pill + '</span>' + chev + '</summary><div class="exp-body">'
        + (a.state === 'missing'
          ? '<div class="exp-line"><span class="txt"><span class="t">Install ' + esc(a.name) + ' first</span><span class="d">BrowserAI looks for it again each time this page opens.</span></span></div>'
          : '<div class="exp-line"><span class="txt"><span class="t">All your projects</span><span class="d">' + (a.state === 'repair' ? 'Points the entry at this install again.' : 'Writes the entry again as BrowserAI writes it, which undoes your own edits to it.') + '</span></span>' + (a.state === 'repair' ? '<button class="btn accent" type="button">Repair</button>' : '<button class="btn" type="button">Set up again</button><button class="btn" type="button">Remove</button>') + '</div>'
            + '<div class="exp-line"><span class="txt"><span class="t">One project</span><span class="d">Writes or removes ' + esc(a.projectFile) + ' in a folder you choose. Windows\' folder picker may open behind this browser.</span></span><button class="btn" type="button">Choose a folder</button></div>'
            + (a.restart ? '<div class="exp-line">' + I.of('info') + '<span class="txt"><span class="d">' + esc(a.restart) + '</span></span></div>' : ''))
        + '</div></details>';
    }
    return html;
  }

  // ---- Storage ----------------------------------------------------------------------

  function storage() {
    const total = M.totalMb;
    const a = (M.sessionsMb / total) * 100;
    const b = (M.browsersMb / total) * 100;
    let html = '<h1 class="page-title">Storage</h1><p class="page-sub">What BrowserAI keeps on this PC. Session folders stay until the agent that made them deletes them; BrowserAI never deletes one on its own.</p>';
    html += '<div class="rows"><div class="srow">' + I.of('disk') + '<span class="txt"><span class="t">' + M.size(total) + ' used</span><svg class="bar" viewBox="0 0 100 8" preserveAspectRatio="none" aria-hidden="true"><rect class="seg-a" x="0" y="0" width="' + a + '" height="8"/><rect class="seg-b" x="' + a + '" y="0" width="' + b + '" height="8"/><rect class="seg-c" x="' + (a + b) + '" y="0" width="' + (100 - a - b) + '" height="8"/></svg><span class="d">Session folders ' + M.size(M.sessionsMb) + ', browsers ' + M.size(M.browsersMb) + ', logs ' + M.size(Math.max(1, D.logsMb)) + '</span></span></div></div>';
    html += '<div class="group-title">Session folders <span>Largest first</span></div>';
    const all = D.sessions.map((s) => ({ name: s.name, purpose: s.purpose, mb: s.mb, used: s.running ? 'now' : s.lastAgo, flags: s.flags, open: s.state !== 'closed' })).concat(D.onDisk.map((s) => ({ name: s.name, purpose: s.purpose, mb: s.mb, used: s.used, flags: s.flags, open: false }))).sort((x, y) => y.mb - x.mb);
    html += all.length ? '<div class="rows">' + all.map((r) => '<div class="srow">' + I.of('folder') + '<span class="txt"><span class="t">' + esc(r.name) + r.flags.map((f) => '<span class="flag">' + esc(f) + '</span>').join('') + '</span><span class="d">' + esc(r.purpose) + ', ' + (r.open ? 'open now' : 'last used ' + esc(r.used)) + '</span></span><span class="side-val">' + M.size(r.mb) + '<button class="btn" type="button">Open folder</button></span></div>').join('') + '</div>'
      : '<div class="empty">' + I.of('folder') + '<div><b>No session folder yet</b><span>Each session an agent starts is a folder it names. They are listed here with their size.</span></div></div>';
    html += '<div class="group-title">Browsers <span>Downloaded once, shared by every session</span></div><div class="rows">' + (D.browsersInstalled.map((x) => '<div class="srow">' + I.of('globe') + '<span class="txt"><span class="t">' + esc(x.name) + '</span><span class="d">' + esc(x.note) + '</span></span><span class="side-val">' + M.size(x.mb) + '</span></div>').join('') || '<div class="srow">' + I.of('globe') + '<span class="txt"><span class="t">None yet</span><span class="d">Chromium downloads on first use, about 209 MB.</span></span></div>') + '</div>';
    return html;
  }

  function files() {
    let html = '<h1 class="page-title">Folders and logs</h1><p class="page-sub">Where BrowserAI lives on this PC.</p>';
    if (D.broken) {
      html += '<div class="infobar bad" role="alert">' + I.of('bad') + '<div class="txt"><b>BrowserAI needs reinstalling</b>The browser server it starts for every session lists different tools from the ones this BrowserAI was built with; the first difference is ' + esc(D.broken.first) + '. Download BrowserAI.exe from the latest release and run it. It installs over this install and keeps every session.</div><a class="btn" href="#">Latest release ' + I.of('external') + '</a></div>';
    }
    const row = (icon, t, path) => '<div class="srow">' + I.of(icon) + '<span class="txt"><span class="t">' + t + '</span><span class="d"><code>' + esc(path) + '</code></span></span><button class="btn" type="button">Open</button></div>';
    html += '<div class="rows">' + row('folder', 'Installed in', D.install.root) + row('disk', 'Data: browsers, the session index and logs', D.install.data) + row('file', 'Logs', D.install.logs) + '</div>';
    html += '<div class="group-title">For troubleshooting</div><div class="rows"><div class="srow">' + I.of('terminal') + '<span class="txt"><span class="t">What Claude Code and Codex start</span><span class="d"><code>' + esc(D.install.server) + '</code></span></span></div><div class="srow">' + I.of('clock') + '<span class="txt"><span class="t">Running since ' + esc(D.since) + '</span><span class="d">Started ' + esc(D.startedHow) + '. It ends at sign-out, at an uninstall or for an update.</span></span></div></div>';
    return html;
  }

  function whatsNew() {
    const c = D.changelog;
    let html = '<h1 class="page-title">What is new in ' + esc(c.version) + '</h1><p class="page-sub">Released ' + esc(c.date) + '.</p>';
    for (const g of c.groups) {
      html += '<div class="group-title">' + esc(g.name) + '</div>' + g.entries.map((e) => '<details class="exp"><summary><span aria-hidden="true">' + e.icon + '</span><span class="txt"><span class="t">' + esc(e.head) + '</span></span>' + chev + '</summary><div class="exp-body"><div class="exp-line"><span class="txt"><span class="d">' + esc(e.body) + '</span></span></div></div></details>').join('');
    }
    return html;
  }

  const routes = { '': home, conversations, sessions, update, apps, storage, files, new: whatsNew };

  function render() {
    const [name, arg] = location.hash.replace(/^#\/?/, '').split('/');
    const key = routes[name] ? name : '';
    const view = routes[key];
    document.getElementById('content').innerHTML = view(arg);
    for (const a of document.querySelectorAll('.nav a')) {
      a.toggleAttribute('aria-current', a.dataset.key === key);
      if (a.dataset.key === key) a.setAttribute('aria-current', 'page');
    }
    const select = document.getElementById('jump');
    if (select) select.value = key;
    M.tick();
    window.scrollTo(0, 0);
  }

  const navLink = (p) => {
    const badge = p.badge ? p.badge() : '';
    return '<a href="#/' + p.key + '" data-key="' + p.key + '">' + I.of(p.icon) + '<span class="label">' + esc(p.label) + '</span>' + (badge ? '<span class="badge' + (p.quiet ? ' quiet' : '') + (p.warn ? ' warn' : '') + '">' + esc(badge) + '</span>' : '') + '</a>';
  };
  document.getElementById('side').innerHTML = '<div class="side-brand">' + I.brand('b') + '<div><b>BrowserAI</b><span>' + esc(D.version) + ', running</span></div></div>'
    + '<nav class="nav" aria-label="Sections">' + pages.map(navLink).join('') + '</nav><div class="nav-gap"></div>'
    + '<div class="side-foot nav"><a href="#" title="Opens the README on GitHub">' + I.of('question') + '<span class="label">How BrowserAI works</span></a></div>';
  document.getElementById('top').innerHTML = I.brand('t') + '<b>BrowserAI</b><select id="jump" aria-label="Section">' + pages.map((p) => '<option value="' + p.key + '">' + esc(p.label) + '</option>').join('') + '</select>';
  document.getElementById('jump').addEventListener('change', (e) => { location.hash = '#/' + e.target.value; });

  window.addEventListener('hashchange', render);
  render();
  M.startTicking();
  M.mockControl();
})();

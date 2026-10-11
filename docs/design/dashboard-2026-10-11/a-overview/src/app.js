// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

(() => {
  const M = MOCK;
  const D = M.data;
  const I = ICON;
  const esc = M.esc;
  M.applyTheme();

  const view = document.getElementById('view');
  const appClass = (app) => (app === 'Codex' ? 'cx' : 'cc');
  const appInitials = (app) => (app === 'Codex' ? 'CX' : 'CC');
  const badge = (app) => '<span class="app ' + appClass(app) + '" aria-hidden="true">' + appInitials(app) + '</span>';
  const count = (ms) => '<span class="count" data-ends="' + ms + '"></span>';
  const sep = '<span class="sep" aria-hidden="true"></span>';
  const plural = (n, one, many) => n + ' ' + (n === 1 ? one : many);
  const crumbs = (...parts) => '<nav class="crumbs" aria-label="Where you are"><a href="#/">Overview</a>' + parts.map((p) => I.of('chevron') + (p.href ? '<a href="' + p.href + '">' + esc(p.label) + '</a>' : '<span aria-current="page">' + esc(p.label) + '</span>')).join('') + '</nav>';

  function browserChip(s) {
    if (s.state === 'window') {
      return '<span class="chip window">' + I.of('window') + 'Window on your screen</span>';
    }
    if (s.state === 'hidden') {
      return '<span class="chip hidden">' + I.of('hidden') + 'Hidden browser' + (s.closes ? ', closes in ' + count(s.closes) : '') + '</span>';
    }
    return '<span class="chip">' + I.of('closed') + 'No browser open</span>';
  }

  function conversationState(c) {
    if (c.running) {
      return '<span class="chip live"><span class="pulse"></span>Working now</span>';
    }
    if (c.holdsUntil) {
      return '<span class="chip">Last call a few minutes ago</span>';
    }
    return '<span class="chip">Idle</span>';
  }

  // ---- The overview -------------------------------------------------------

  function headline() {
    if (M.attention.length > 0) {
      return { h: plural(M.attention.length, 'thing needs you', 'things need you'), p: 'BrowserAI is running. Each item below says what is wrong and what to do about it.' };
    }
    if (D.occasion === 'firstrun') {
      return { h: 'BrowserAI is installed', p: 'It runs in the background from now on and gives your agents a real browser. Below is what it set up.' };
    }
    if (D.conversations.length === 0) {
      return { h: 'All quiet', p: 'BrowserAI is running and ready. No conversation is using it right now.' };
    }
    const n = D.conversations.length;
    const w = M.windows.length;
    return {
      h: plural(n, 'conversation is', 'conversations are') + ' using BrowserAI',
      p: (w > 0 ? (w === 1 ? 'One of them has a browser window' : w + ' of them have browser windows') + ' open on your screen. ' : '')
        + (D.update.state === 'held' ? 'Update ' + D.update.version + ' installs by itself when they are done.' : 'Everything is up to date.'),
    };
  }

  function notices() {
    let html = '';
    for (const a of M.attention) {
      const icon = a.level === 'bad' ? 'bad' : 'warn';
      const action = a.kind === 'broken' ? '<a class="btn" href="#/install">How to reinstall</a>'
        : a.kind === 'app' ? '<a class="btn" href="#/apps">Repair</a>'
        : '<a class="btn" href="#/update">See the update</a>';
      html += '<div class="notice ' + a.level + '" role="alert">' + I.of(icon) + '<div class="grow"><h3>' + esc(a.title) + '</h3><p>' + esc(a.text) + '</p></div>' + action + '</div>';
    }
    if (D.occasion === 'firstrun') {
      html += '<div class="notice good">' + I.of('checkCircle') + '<div class="grow"><h3>Set up for Claude Code</h3><p>Claude Code sessions you start from now on can use BrowserAI. Sessions that were already open see it after a restart. Codex was not found on this PC.</p></div><a class="btn" href="#/apps">See the setup</a></div>';
    }
    return html;
  }

  function nowCard() {
    const n = D.conversations.length;
    let body;
    if (n === 0 && D.sessions.length === 0) {
      body = '<div class="empty">' + I.of('chat') + '<p class="empty-title">No conversation is using BrowserAI</p><p>When an agent in Claude Code or Codex calls a BrowserAI tool, its conversation appears here, with the browser it opened.</p></div>';
    } else {
      const kept = D.sessions.filter((s) => s.kept);
      const stats = '<div class="stats">'
        + '<div class="stat">' + I.of('chat') + '<div><b>' + n + '</b><span>' + (n === 1 ? 'Conversation' : 'Conversations') + '</span></div></div>'
        + '<div class="stat' + (M.windows.length ? ' warn' : '') + '">' + I.of('window') + '<div><b>' + M.windows.length + '</b><span>On your screen</span></div></div>'
        + '<div class="stat">' + I.of('hidden') + '<div><b>' + M.hidden.length + '</b><span>Hidden ' + (M.hidden.length === 1 ? 'browser' : 'browsers') + '</span></div></div>'
        + '<div class="stat">' + I.of('folder') + '<div><b>' + kept.length + '</b><span>Kept for later</span></div></div>'
        + '</div>';
      const rows = D.conversations.slice(0, 4).map((c) => {
        const chips = c.sessions.map((id) => browserChip(M.sessionById(id))).join('') || '<span class="chip">No browser yet</span>';
        const working = c.running ? sep + '<span class="working"><span class="pulse"></span>Working now</span>' : '';
        return '<a class="row" href="#/now">' + badge(c.app) + '<span class="row-main"><span class="row-title">' + esc(c.title) + '</span><span class="row-sub">' + esc(M.placeText[c.place]) + sep + esc(c.folderName) + working + '</span></span><span class="row-side">' + chips + '</span>' + I.of('chevron') + '</a>';
      });
      for (const s of kept) {
        rows.push('<a class="row" href="#/session/' + s.id + '"><span class="app kept" aria-hidden="true">' + I.of('folder') + '</span><span class="row-main"><span class="row-title">' + esc(s.name) + '</span><span class="row-sub">Kept after its conversation ended</span></span><span class="row-side">' + browserChip(s) + '</span>' + I.of('chevron') + '</a>');
      }
      body = stats + '<div class="rows">' + rows.join('') + '</div>';
    }
    return '<article class="card span-8"><div class="card-head">' + I.of('pulse') + '<h2>Right now</h2><a class="more" href="#/now">See all' + I.of('chevron') + '</a></div>'
      + '<p class="caption">Conversations in Claude Code and Codex that use BrowserAI, and the browsers they opened.</p>' + body + '</article>';
  }

  function updateCard() {
    const u = D.update;
    let body;
    if (u.state === 'held' && u.wait === 'counting') {
      const active = D.conversations.filter((c) => c.running || c.holdsUntil).length;
      const list = M.windows.map((s) => '<div class="mini"><span class="mini-ic window">' + I.of('window') + '</span><span>' + esc(s.name) + '</span>' + count(s.closes) + '</div>').join('')
        + M.hidden.map((s) => '<div class="mini"><span class="mini-ic hidden">' + I.of('hidden') + '</span><span>' + esc(s.name) + '</span>' + count(s.closes) + '</div>').join('')
        + (active ? '<div class="mini"><span class="mini-ic">' + I.of('chat') + '</span><span>' + plural(active, 'active conversation', 'active conversations') + '</span><span class="small">10 min after each call</span></div>' : '');
      body = '<p class="big">' + esc(u.version) + ' is ready</p><p>It installs by itself in ' + count(u.installsAt) + ', at about ' + M.clockOf(u.installsAt) + ', once nothing below uses BrowserAI.</p>'
        + '<div class="minis">' + list + '</div><div class="btn-row"><a class="btn primary" href="#/update">See what holds it</a></div>';
    } else if (u.state === 'held') {
      body = '<p class="big">' + esc(u.version) + ' is waiting</p><p>A window set never to close by itself holds it. It installs once you close that window.</p><div class="btn-row"><a class="btn primary" href="#/update">See the update</a></div>';
    } else {
      body = '<p class="big">Up to date</p><p>' + esc(D.version) + ' is the newest version. BrowserAI checked ' + esc(u.source) + ' at ' + esc(u.checked) + '.</p><p class="small">When a newer version is out, BrowserAI downloads it and installs it once nothing uses BrowserAI.</p>';
    }
    return '<article class="card span-4"><div class="card-head">' + I.of('update') + '<h2>Update</h2></div>' + body + '</article>';
  }

  function stateOf(app) {
    if (app.state === 'ok') return '<span class="state ok">' + I.of('checkCircle') + esc(app.short) + '</span>';
    if (app.state === 'repair') return '<span class="state warn">' + I.of('warn') + esc(app.short) + '</span>';
    return '<span class="state missing">' + I.of('minus') + esc(app.short) + '</span>';
  }

  function appsCard() {
    const rows = D.apps.map((a) => '<div class="app-row">' + badge(a.name) + '<div><b>' + esc(a.name) + '</b>' + stateOf(a) + '</div></div>').join('');
    return '<article class="card span-4"><div class="card-head">' + I.of('plug') + '<h2>Claude Code and Codex</h2><a class="more" href="#/apps">Manage' + I.of('chevron') + '</a></div>'
      + '<p class="caption">Your agents reach BrowserAI through these apps.</p><div class="app-rows">' + rows + '</div></article>';
  }

  function storageCard() {
    const total = M.totalMb;
    const a = (M.sessionsMb / total) * 100;
    const b = (M.browsersMb / total) * 100;
    const bar = '<svg class="bar" viewBox="0 0 100 10" preserveAspectRatio="none" role="img" aria-label="Session folders, browsers and logs as parts of the total"><rect class="seg-a" x="0" y="0" width="' + a + '" height="10"/><rect class="seg-b" x="' + a + '" y="0" width="' + b + '" height="10"/><rect class="seg-c" x="' + (a + b) + '" y="0" width="' + (100 - a - b) + '" height="10"/></svg>';
    const legend = '<div class="legend"><div><span class="swatch a"></span><span>' + plural(M.folderCount, 'session folder', 'session folders') + '</span><b>' + M.size(M.sessionsMb) + '</b></div>'
      + '<div><span class="swatch b"></span><span>' + (D.browsersInstalled.length ? 'Browsers' : 'Browsers, none downloaded yet') + '</span><b>' + M.size(M.browsersMb) + '</b></div>'
      + '<div><span class="swatch c"></span><span>Logs</span><b>' + M.size(Math.max(1, D.logsMb)) + '</b></div></div>';
    return '<article class="card span-4"><div class="card-head">' + I.of('disk') + '<h2>Storage</h2><a class="more" href="#/storage">Details' + I.of('chevron') + '</a></div>'
      + '<p class="big">' + M.size(total) + '</p><p class="caption">used by BrowserAI on this PC</p>' + bar + legend + '</article>';
  }

  function installCard() {
    return '<article class="card span-4"><div class="card-head">' + I.of('info') + '<h2>This install</h2><a class="more" href="#/install">Paths' + I.of('chevron') + '</a></div>'
      + '<dl class="facts"><dt>Version</dt><dd>' + esc(D.version) + ', <a href="#/changelog">what is new</a></dd><dt>Running</dt><dd>since ' + esc(D.since) + ', started ' + esc(D.startedHow) + '</dd><dt>Updates</dt><dd>from ' + esc(D.update.source) + '</dd></dl>'
      + '<div class="btn-row"><button class="btn" type="button">' + I.of('folder') + 'Install folder</button><button class="btn" type="button">' + I.of('folder') + 'Data</button><button class="btn" type="button">' + I.of('file') + 'Logs</button></div></article>';
  }

  function home() {
    const h = headline();
    return '<section class="hello"><h1>' + esc(h.h) + '</h1><p class="lede">' + esc(h.p) + '</p></section>' + notices()
      + '<div class="grid">' + nowCard() + updateCard() + appsCard() + storageCard() + installCard() + '</div>';
  }

  // ---- Conversations and browsers ----------------------------------------

  function sessionRow(s) {
    const kind = s.state === 'window' ? 'window' : s.state === 'hidden' ? 'hidden' : '';
    const icon = s.state === 'window' ? 'window' : s.state === 'hidden' ? 'hidden' : 'closed';
    let line;
    if (s.state === 'window') {
      line = s.never ? 'A ' + s.browser + ' window on your screen, set never to close by itself.' : 'A ' + s.browser + ' window on your screen. It closes by itself in ' + count(s.closes) + ' if nobody uses it.';
    } else if (s.state === 'hidden') {
      line = 'A hidden ' + s.browser + '. It closes in ' + count(s.closes) + ' if no call uses it.';
    } else {
      line = 'Its ' + s.browser + ' ' + (s.closedWhy || 'is closed') + '. The session folder stays.';
    }
    return '<div class="sess"><span class="sess-ic ' + kind + '">' + I.of(icon) + '</span><div><div class="sess-name">' + esc(s.name) + '</div><div class="sess-purpose">' + esc(s.purpose) + '</div><div class="sess-line">' + line + '</div></div>'
      + '<div class="sess-actions"><button class="btn quiet" type="button">' + I.of('folder') + 'Open folder</button><a class="btn" href="#/session/' + s.id + '">Details</a></div></div>';
  }

  function conversationCard(c) {
    const sessions = c.sessions.map((id) => sessionRow(M.sessionById(id))).join('')
      || '<div class="none-yet">No browser yet. This conversation is connected and has not opened a session.</div>';
    const status = c.running ? '<span class="chip live"><span class="pulse"></span>Working now</span>' : c.holdsUntil ? '<span class="chip">Last call a few minutes ago</span>' : '<span class="chip">Idle</span>';
    return '<article class="card convo"><div class="convo-head">' + badge(c.app) + '<div><h3>' + esc(c.title) + '</h3><div class="row-sub">' + esc(c.app + ' ' + c.version) + sep + esc(c.place === 'Terminal' ? 'in a terminal' : c.place === 'VS Code' ? 'in VS Code' : 'Codex app') + sep + '<code>' + esc(c.folder) + '</code></div></div><div class="right">' + status + '</div></div>'
      + sessions + '<div class="convo-foot">' + I.of('info') + esc(M.reconnectText[c.reconnect]) + '</div></article>';
  }

  function now() {
    let html = crumbs({ label: 'Conversations and browsers' }) + '<h1 class="page-title">Conversations and browsers</h1><p class="lede">Every chat in Claude Code or Codex that uses BrowserAI, and the browser sessions it opened. Nothing here can be closed from this page: an agent closes its own browser, and a conversation ends when its app closes it.</p>';
    html += '<div class="explain"><div>' + I.of('chat') + '<div><b>Conversation</b><p>A chat in Claude Code or Codex. It is connected to BrowserAI while it is open.</p></div></div>'
      + '<div>' + I.of('folder') + '<div><b>Session</b><p>A folder an agent made for its browser work: the browser profile with its logins, screenshots, downloads and a log.</p></div></div>'
      + '<div>' + I.of('globe') + '<div><b>Browser</b><p>A session\'s browser while it runs. Hidden, or a window on your screen. It closes by itself when nobody uses it.</p></div></div></div>';
    if (D.conversations.length === 0 && D.sessions.length === 0) {
      return html + '<div class="empty">' + I.of('chat') + '<p class="empty-title">No conversation is using BrowserAI</p><p>Start a conversation in Claude Code or Codex and ask it to use a browser. It appears here as soon as it calls a BrowserAI tool.</p></div>';
    }
    const groups = [];
    const seen = new Set();
    for (const c of D.conversations) {
      if (c.windowKey) {
        if (seen.has(c.windowKey)) continue;
        seen.add(c.windowKey);
        groups.push({ head: c.windowLabel, icon: 'code', items: D.conversations.filter((x) => x.windowKey === c.windowKey) });
      } else {
        groups.push({ head: c.place === 'Terminal' ? 'Terminal' : 'Codex', icon: c.place === 'Terminal' ? 'terminal' : 'chat', items: [c] });
      }
    }
    for (const g of groups) {
      html += '<section class="group"><div class="group-head">' + I.of(g.icon) + esc(g.head) + '</div>' + g.items.map(conversationCard).join('') + '</section>';
    }
    const kept = D.sessions.filter((s) => s.kept);
    if (kept.length) {
      html += '<section class="group"><div class="group-head">' + I.of('folder') + 'Kept after their conversation ended</div><article class="card convo"><div class="convo-head"><div><h3>' + plural(kept.length, 'session', 'sessions') + ' with no conversation</h3><div class="row-sub">BrowserAI keeps the browser as it was, so a conversation can pick the session up again. Its idle close ends it otherwise.</div></div></div>' + kept.map(sessionRow).join('') + '</article></section>';
    }
    return html;
  }

  // ---- One session ----------------------------------------------------------

  function session(id) {
    const s = M.sessionById(id) || D.sessions[0];
    if (!s) return now();
    const c = s.by ? M.conversationById(s.by) : null;
    const browserLine = s.state === 'window' ? (s.never ? 'A window on your screen, set never to close by itself.' : 'A window on your screen. It closes by itself in ' + count(s.closes) + ' if nobody types, clicks or calls in it.')
      : s.state === 'hidden' ? 'Hidden. It closes in ' + count(s.closes) + ' if no call uses it, and the session folder stays.'
      : 'Not open. It ' + esc(s.closedWhy || 'was closed') + '. A call that names the session opens it again.';
    const holds = [];
    if (s.flags.includes('transcript')) holds.push(['file', 'A transcript', 'Every browser call and its result, and what was typed into pages, passwords included.']);
    if (s.flags.includes('saved login')) holds.push(['key', 'A saved login', 'A file with the cookies and storage of a signed-in site.']);
    if (s.flags.includes('network recording')) holds.push(['shield', 'A network recording', 'Every request and response with their headers, sign-in tokens included.']);
    if (s.traces) holds.push(['list', plural(s.traces, 'trace', 'traces'), 'A recording of the page that Playwright\'s trace viewer can replay.']);
    holds.push(['globe', 'A browser profile', 'Logins and cookies of every site the browser visited, until the folder is deleted.']);
    return crumbs({ label: 'Conversations and browsers', href: '#/now' }, { label: s.name })
      + '<h1 class="page-title">' + esc(s.name) + '</h1><p class="lede">A browser session. Its agent wrote that it is for <b>' + esc(s.purpose) + '</b>.</p>'
      + '<div class="two"><div>'
      + '<div class="box"><h2>Browser</h2><p><b>' + esc(s.browser) + '</b></p><p>' + browserLine + '</p></div>'
      + '<div class="box"><h2>Used by</h2>' + (c ? '<div class="app-row">' + badge(c.app) + '<div><b>' + esc(c.title) + '</b><span class="small">' + esc(M.placeText[c.place]) + ', ' + esc(c.folderName) + '</span></div></div>' : '<p>No conversation. The conversation that used it has ended, and BrowserAI keeps it for the next one that names it.</p>') + '</div>'
      + '<div class="box"><h2>Latest call</h2><p class="why">' + esc(s.lastWhy) + '</p><p class="small">' + (s.running ? 'Running now.' : esc(s.lastAgo) + '.') + ' The agent writes why it makes each call, and the session\'s log keeps every one.</p></div>'
      + '</div><div>'
      + '<div class="box"><h2>Folder</h2><div class="path"><code>' + esc(s.folder) + '</code><button class="btn" type="button">' + I.of('folder') + 'Open</button></div><div class="kv"><span>Size</span><b>' + M.size(s.mb) + '</b></div></div>'
      + '<div class="box"><h2>What the folder holds</h2><div class="holds">' + holds.map((h) => '<div class="hold' + (h[0] === 'globe' || h[0] === 'list' ? ' calm' : '') + '">' + I.of(h[0]) + '<div><b>' + esc(h[1]) + '</b><br><span class="small">' + esc(h[2]) + '</span></div></div>').join('') + '</div></div>'
      + '<div class="notice good">' + I.of('info') + '<div class="grow"><p>Only the agent that made a session deletes it. BrowserAI never deletes a session folder on its own.</p></div></div>'
      + '</div></div>';
  }

  // ---- The update -------------------------------------------------------------

  function update() {
    const u = D.update;
    let html = crumbs({ label: 'Update' });
    if (u.state !== 'held') {
      return html + '<h1 class="page-title">BrowserAI is up to date</h1><p class="lede">' + esc(D.version) + ' is the newest version. BrowserAI checked ' + esc(u.source) + ' at ' + esc(u.checked) + '. When a newer version is out, it is downloaded and installed once nothing uses BrowserAI, and a notification says so.</p>'
        + '<div class="empty">' + I.of('checkCircle') + '<p class="empty-title">Nothing is waiting</p><p>When an update waits, this page shows what is still using BrowserAI, with a countdown for each, and a button to install it now.</p></div>';
    }
    html += '<h1 class="page-title">Update ' + esc(u.version) + ' is ready to install</h1><p class="lede">BrowserAI downloaded it at 13:36. It installs by itself once nothing uses BrowserAI, so no agent is cut off in the middle of its work.</p>';
    html += '<div class="card countdown-hero">' + (u.wait === 'counting'
      ? '<div class="clock">' + count(u.installsAt) + '</div><div><p><b>Installs by itself at about ' + M.clockOf(u.installsAt) + '</b></p><p class="small">if nothing below is used before then. Each countdown starts again when it is used.</p></div>'
      : '<div class="clock">' + I.of('window') + '</div><div><p><b>Waiting for a window</b></p><p class="small">A window set never to close by itself holds the update. It installs once you close that window and nothing else uses BrowserAI.</p></div>')
      + '<div class="hero-act"><button class="btn danger" type="button">Install ' + esc(u.version) + ' now</button><span class="small">Closes every browser and ends every connection. Below is what that means.</span></div></div>';
    html += '<div class="grid">';
    const item = (icon, cls, title, sub, side) => '<div class="hold-item"><span class="sess-ic ' + cls + '">' + I.of(icon) + '</span><div><b>' + title + '</b><div class="sub">' + sub + '</div></div><div>' + side + '</div></div>';
    const win = M.windows.map((s) => item('window', 'window', esc(s.name), 'Close this window to let the update go ahead.', s.never ? '<span class="chip window">Never closes</span>' : '<span class="chip window">' + count(s.closes) + '</span>')).join('') || '<p class="small">No window is open.</p>';
    const hid = M.hidden.map((s) => item('hidden', 'hidden', esc(s.name), s.kept ? 'Kept after its conversation ended.' : 'Closes by itself if no call uses it.', '<span class="chip hidden">' + count(s.closes) + '</span>')).join('') || '<p class="small">No hidden browser is open.</p>';
    const act = D.conversations.map((c) => item(c.place === 'Terminal' ? 'terminal' : c.place === 'VS Code' ? 'code' : 'chat', '', esc(c.title), esc(M.reconnectText[c.reconnect]), c.running ? '<span class="chip live"><span class="pulse"></span>Working</span>' : c.holdsUntil ? '<span class="chip">' + count(c.holdsUntil) + '</span>' : '<span class="chip">Idle</span>')).join('') || '<p class="small">No conversation is connected.</p>';
    html += '<article class="card span-4"><div class="card-head">' + I.of('window') + '<h2>Windows on your screen</h2></div><p class="caption">Each one closes after an hour nobody uses it.</p><div class="hold-list">' + win + '</div></article>';
    html += '<article class="card span-4"><div class="card-head">' + I.of('hidden') + '<h2>Hidden browsers</h2></div><p class="caption">Each one closes after its idle minutes.</p><div class="hold-list">' + hid + '</div></article>';
    html += '<article class="card span-4"><div class="card-head">' + I.of('chat') + '<h2>Conversations</h2></div><p class="caption">Each holds the update for ten minutes after its last call.</p><div class="hold-list">' + act + '</div></article>';
    html += '<article class="card span-12"><div class="card-head">' + I.of('bolt') + '<h2>If you install it now</h2></div><ul class="effects"><li>Every browser closes cleanly, windows included, with what is open in them. Each session comes back where it was when its agent resumes it.</li><li>Every conversation loses BrowserAI for a moment. Claude Code in VS Code reconnects by itself. In a terminal, run /mcp, choose BrowserAI, then Reconnect. A Codex conversation gets BrowserAI back only in a new conversation.</li><li>BrowserAI starts again by itself once ' + esc(u.version) + ' is installed, and a notification says so.</li></ul><button class="btn danger" type="button">Install ' + esc(u.version) + ' now</button></article>';
    return html + '</div>';
  }

  // ---- Claude Code and Codex ----------------------------------------------------

  function apps() {
    let html = crumbs({ label: 'Claude Code and Codex' }) + '<h1 class="page-title">Claude Code and Codex</h1><p class="lede">Your agents reach BrowserAI through these apps, and each is set up on its own. Set up for all your projects means one entry in your own settings of that app. A project setup writes a file in one folder, to commit with the project.</p>';
    for (const a of D.apps) {
      const primary = a.state === 'ok' ? '<button class="btn" type="button">Set up again</button>' : a.state === 'repair' ? '<button class="btn primary" type="button">Repair ' + esc(a.name) + '</button>' : '<button class="btn" type="button" disabled>Install ' + esc(a.name) + ' first</button>';
      const remove = a.state === 'ok' ? '<button class="btn quiet" type="button">Remove from ' + esc(a.name) + '</button>' : '';
      html += '<article class="card app-card"><div class="head">' + badge(a.name) + '<h2>' + esc(a.name) + '</h2>' + stateOf(a) + '</div><p>' + esc(a.sentence) + '</p>' + (a.restart ? '<p class="small">' + esc(a.restart) + '</p>' : '')
        + '<div class="btn-row">' + primary + remove + '</div>'
        + (a.state !== 'missing' ? '<div class="sub-block"><h3>In one project</h3><p>Writes or removes ' + esc(a.projectFile) + ' in a folder you choose. Windows\' folder picker opens, and it can open behind this browser window.</p><div class="btn-row"><button class="btn" type="button">Set up in a project</button><button class="btn quiet" type="button">Remove from a project</button></div></div>' : '')
        + '<details class="more-info"><summary>Details for troubleshooting</summary><pre>' + esc((a.found ? a.name + ' found: ' + a.found + '\n' : '') + 'Entry: browserai, starting ' + D.install.server) + '</pre></details></article>';
    }
    return html;
  }

  // ---- Storage --------------------------------------------------------------------

  function storage() {
    let html = crumbs({ label: 'Storage' }) + '<h1 class="page-title">Storage</h1><p class="lede">BrowserAI keeps three kinds of things on this PC. Session folders stay until the agent that made them deletes them; BrowserAI never deletes one on its own. To remove one yourself, open its folder.</p>';
    html += '<div class="grid"><article class="card span-4"><div class="card-head">' + I.of('folder') + '<h2>Session folders</h2></div><p class="big">' + M.size(M.sessionsMb) + '</p><p class="small">' + plural(M.folderCount, 'folder', 'folders') + ', each where its agent put it</p></article>'
      + '<article class="card span-4"><div class="card-head">' + I.of('globe') + '<h2>Browsers</h2></div><p class="big">' + M.size(M.browsersMb) + '</p><p class="small">' + (D.browsersInstalled.length ? D.browsersInstalled.map((b) => esc(b.name)).join(' and ') + ', downloaded once and shared by every session' : 'Downloaded on first use: Chromium is about 209 MB') + '</p></article>'
      + '<article class="card span-4"><div class="card-head">' + I.of('file') + '<h2>Logs</h2></div><p class="big">' + M.size(Math.max(1, D.logsMb)) + '</p><p class="small">BrowserAI\'s own log, in the data folder</p></article></div>';
    const all = D.sessions.map((s) => ({ name: s.name, purpose: s.purpose, mb: s.mb, used: s.running ? 'now' : s.lastAgo, open: s.state !== 'closed', flags: s.flags, id: s.id }))
      .concat(D.onDisk.map((s) => ({ name: s.name, purpose: s.purpose, mb: s.mb, used: s.used, open: false, flags: s.flags })))
      .sort((x, y) => y.mb - x.mb);
    if (all.length === 0) {
      return html + '<div class="empty spaced">' + I.of('folder') + '<p class="empty-title">No session folder yet</p><p>Each time an agent starts a browser session, it names a folder for it. The folders are listed here with their size.</p></div>';
    }
    html += '<article class="card spaced"><div class="card-head">' + I.of('list') + '<h2>Every session folder</h2></div><p class="caption">Largest first. A folder can hold a login or a password; the column on the right says which.</p><table class="table"><thead><tr><th>Session</th><th class="hide-narrow">Browser</th><th>Last used</th><th class="num">Size</th><th class="hide-narrow">Holds</th><th></th></tr></thead><tbody>'
      + all.map((r) => '<tr><td><b>' + esc(r.name) + '</b><br><span class="small">' + esc(r.purpose) + '</span></td><td class="hide-narrow">' + (r.open ? '<span class="chip hidden">Open now</span>' : '<span class="small">Closed</span>') + '</td><td>' + esc(r.used) + '</td><td class="num">' + M.size(r.mb) + '</td><td class="hide-narrow">' + (r.flags.map((f) => '<span class="flag">' + esc(f) + '</span>').join('') || '<span class="small">nothing marked</span>') + '</td><td><button class="btn quiet" type="button">' + I.of('folder') + 'Open</button></td></tr>').join('')
      + '</tbody></table></article>';
    return html;
  }

  // ---- This install ---------------------------------------------------------------

  function installPage() {
    let html = crumbs({ label: 'This install' }) + '<h1 class="page-title">This install</h1><p class="lede">Where BrowserAI lives on this PC, and what Claude Code and Codex start to reach it.</p>';
    if (D.broken) {
      html += '<div class="notice bad" role="alert">' + I.of('bad') + '<div class="grow"><h3>BrowserAI needs reinstalling</h3><p>The browser server it starts for every session lists different tools from the ones this BrowserAI was built with. The first difference is ' + esc(D.broken.first) + '. Download BrowserAI.exe from the latest release and run it. It installs over this install and keeps every session.</p></div><a class="btn" href="#">Latest release' + I.of('external') + '</a></div>';
    }
    const path = (label, value) => '<div class="box"><h2>' + label + '</h2><div class="path"><code>' + esc(value) + '</code><button class="btn" type="button">' + I.of('folder') + 'Open</button></div></div>';
    html += '<div class="two"><div>' + path('Installed in', D.install.root) + path('Data: browsers, the session index and logs', D.install.data) + path('Logs', D.install.logs) + '</div><div>'
      + '<div class="box"><h2>BrowserAI</h2><div class="kv"><span>Version</span><b>' + esc(D.version) + '</b></div><div class="kv"><span>Running since</span><b>' + esc(D.since) + ', started ' + esc(D.startedHow) + '</b></div><div class="kv"><span>Updates from</span><b>' + esc(D.update.source) + '</b></div><div class="kv"><span>Last check</span><b>' + esc(D.update.checked) + '</b></div></div>'
      + '<div class="box"><h2>What Claude Code and Codex start</h2><p><code>' + esc(D.install.server) + '</code></p><p class="small">Started with no argument, the same file opens this page.</p></div>'
      + '</div></div>';
    return html;
  }

  // ---- What's new ------------------------------------------------------------------

  function changelog() {
    const c = D.changelog;
    let html = crumbs({ label: 'What is new in ' + c.version }) + '<h1 class="page-title">What is new in BrowserAI ' + esc(c.version) + '</h1><p class="lede">Released ' + esc(c.date) + '. Open a line to read more.</p>';
    for (const g of c.groups) {
      html += '<section class="log-group"><h2>' + esc(g.name) + '</h2>' + g.entries.map((e) => '<details class="entry"><summary><span aria-hidden="true">' + e.icon + '</span><b>' + esc(e.head) + '</b></summary><p>' + esc(e.body) + '</p></details>').join('') + '</section>';
    }
    return html;
  }

  const routes = { '': home, now, session, update, apps, storage, install: installPage, changelog };

  function render() {
    const [name, arg] = location.hash.replace(/^#\/?/, '').split('/');
    view.innerHTML = (routes[name] || home)(arg);
    M.tick(view);
    window.scrollTo(0, 0);
  }

  const statusDot = M.attention.some((a) => a.level === 'bad') ? 'bad' : M.attention.length ? 'warn' : '';
  document.getElementById('top').innerHTML = '<div class="top-in"><a class="brand" href="#/">' + I.brand('h') + 'BrowserAI <span class="version">' + esc(D.version) + '</span></a><span class="top-gap"></span>'
    + '<span class="running"><span class="dot ' + statusDot + '"></span>' + (statusDot ? 'Needs you' : 'Running') + (statusDot ? '' : ' <span class="long">since ' + esc(D.since) + '</span>') + '</span>'
    + '<a class="help" href="#" title="Opens the README on GitHub">' + I.of('question') + '<span>How BrowserAI works</span></a></div>';
  document.getElementById('foot').innerHTML = '<span>BrowserAI ' + esc(D.version) + '</span><a href="#/changelog">What is new</a><a href="#/install">This install</a>';

  window.addEventListener('hashchange', render);
  render();
  M.startTicking();
  M.mockControl();
})();

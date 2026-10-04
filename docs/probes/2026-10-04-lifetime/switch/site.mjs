// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A local test site on 127.0.0.1 with a cookie-based sign-in, pages that write
// every kind of browser storage, a form, history pages and a tall page with
// row markers. No real site and no real credential: the account is "tester"
// with the password "not-a-secret", and the server forgets it when it exits.

import { createServer } from 'node:http';
import { randomBytes } from 'node:crypto';

const page = (title, body, script = '') =>
  `<!doctype html><html><head><meta charset=utf-8><title>${title}</title></head><body>${body}<script>${script}</script></body></html>`;

const parseCookies = (header) => {
  const jar = {};
  for (const part of (header ?? '').split(';')) {
    const at = part.indexOf('=');
    if (at > 0) jar[part.slice(0, at).trim()] = part.slice(at + 1).trim();
  }
  return jar;
};

export async function startSite(run) {
  const signedIn = new Map(); // auth token -> user
  const sessionTokens = new Set();
  const hits = [];

  const server = createServer((req, res) => {
    const url = new URL(req.url, 'http://127.0.0.1');
    const jar = parseCookies(req.headers.cookie);
    hits.push({ t: Date.now(), method: req.method, path: url.pathname + url.search, cookies: Object.keys(jar), ua: req.headers['user-agent'] ?? null, chUa: req.headers['sec-ch-ua'] ?? null, chPlatform: req.headers['sec-ch-ua-platform'] ?? null });
    const html = (status, body, headers = {}) => {
      res.writeHead(status, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store', ...headers });
      res.end(body);
    };

    if (url.pathname === '/login' && req.method === 'POST') {
      let body = '';
      req.on('data', (c) => { body += c; });
      req.on('end', () => {
        const form = new URLSearchParams(body);
        if (form.get('user') === 'tester' && form.get('pass') === 'not-a-secret') {
          const auth = randomBytes(12).toString('hex');
          const sess = randomBytes(12).toString('hex');
          signedIn.set(auth, 'tester');
          sessionTokens.add(sess);
          res.writeHead(303, {
            location: '/account',
            'cache-control': 'no-store',
            'set-cookie': [
              `auth=${auth}; Path=/; HttpOnly; Max-Age=86400; SameSite=Lax`,
              `sess=${sess}; Path=/; HttpOnly; SameSite=Lax`,
            ],
          });
          res.end();
        } else {
          html(401, page('login failed', '<p id=state>login failed</p>'));
        }
      });
      return;
    }

    if (url.pathname === '/login') {
      return html(200, page('login', '<form method=post action=/login><label>User <input id=user name=user></label> <label>Password <input id=pass name=pass type=password></label> <button id=go type=submit>Sign in</button></form>'));
    }

    if (url.pathname === '/whoami') {
      res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
      res.end(JSON.stringify({
        user: signedIn.get(jar.auth) ?? null,
        sessionCookie: sessionTokens.has(jar.sess),
        jsPersistent: jar.jsp ?? null,
        jsSession: jar.jss ?? null,
        cookieNames: Object.keys(jar).sort(),
      }));
      return;
    }

    if (url.pathname === '/account') {
      const user = signedIn.get(jar.auth);
      return html(200, page(user ? `account: signed in as ${user}` : 'account: signed out', `<p id=state>${user ? `signed in as ${user}` : 'signed out'}</p>`));
    }

    if (url.pathname.startsWith('/h/')) {
      const n = url.pathname.slice(3);
      return html(200, page(`history ${n}`, `<p>history page ${n}</p>`));
    }

    // Passive pages: nothing on them writes any state when it loads, so a page the
    // browser reloads during a session restore cannot write a value back and make
    // it look kept. The rig writes every store through browser_evaluate.
    if (url.pathname === '/store') {
      return html(200, page('store', '<p id=state>store</p>'));
    }

    if (url.pathname === '/form') {
      return html(200, page('form', '<label>Notes <textarea id=notes rows=3 cols=40></textarea></label> <label>Field <input id=field></label>'));
    }

    if (url.pathname === '/tall') {
      // Bands of 100 px, each a solid colour that encodes its own index in R and
      // G (index = R + 256 * G) with B fixed at 0x80, plus the index as text.
      const height = Number(url.searchParams.get('h') ?? '50000');
      const band = 100;
      const bands = Math.ceil(height / band);
      const css = 'html,body{margin:0;padding:0;background:#fff}div.b{height:100px;position:relative;font:bold 40px monospace;color:#000}div.b span{position:absolute;left:220px;top:25px;background:#fff;padding:0 6px}div.k{position:absolute;left:0;top:0;width:200px;height:100px}';
      return html(200, `<!doctype html><html><head><meta charset=utf-8><title>tall ${height}</title><style>${css}</style></head><body><div id=root></div><script>
        const root = document.getElementById('root');
        const parts = [];
        for (let i = 0; i < ${bands}; i++) {
          const r = i % 256, g = Math.floor(i / 256);
          parts.push('<div class=b><div class=k style="background:rgb(' + r + ',' + g + ',128)"></div><span>ROW ' + String(i).padStart(4, '0') + ' y=' + (i * ${band}) + '</span></div>');
        }
        root.innerHTML = parts.join('');
        document.title = 'tall ready ' + document.documentElement.scrollHeight;
      </script></body></html>`);
    }

    return html(200, page('home', '<p>home</p>'));
  });

  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  return { origin: `http://127.0.0.1:${server.address().port}`, hits, close: () => new Promise((r) => server.close(r)) };
}

// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.App.Page;

/// <summary>The page's stylesheet and script, served from the page's own origin.</summary>
/// <remarks>
/// <para>
/// <b>Kept in C# and not as files</b>, so the character rule, the wording rule and
/// the licence header reach them through the same scans that read the rest of the
/// product, and so the executable stays one file. Nothing here is inline in the
/// HTML: the content security policy admits scripts and styles from this origin
/// only.
/// </para>
/// <para>
/// <b>The script does five things and no more</b>: it keeps one event stream open
/// and replaces the page's main part with each state it is sent; it closes the tab
/// when a newer one has replaced it (Q337 a), falling back to a sentence when the
/// browser will not close it; it shows the last thing the coordinator says before
/// it goes; it posts the action a button names, as JSON, to this origin; and once
/// a second it rewrites every countdown from the deadline the element carries,
/// <c>data-ends-at</c>, in milliseconds since 1970 (added 2026-10-08 with the
/// update page). It keeps which <i>Show details</i> are open and which boxes are
/// ticked across a replacement, so a state arriving mid-click does not undo the
/// click.
/// </para>
/// </remarks>
internal static class PageAssets
{
    /// <summary>The stylesheet.</summary>
    public const string Css = """
        :root { color-scheme: light dark; }
        body { font: 15px/1.45 system-ui, sans-serif; margin: 0 auto; max-width: 56rem; padding: 1.5rem; }
        nav { display: flex; gap: 1rem; margin-bottom: 1rem; }
        nav a[aria-current="page"] { font-weight: 600; text-decoration: none; }
        h1 { font-size: 1.5rem; margin: 0 0 1rem; }
        h2 { font-size: 1.15rem; margin: 1.5rem 0 0.5rem; }
        code { font-size: 0.92em; overflow-wrap: anywhere; }
        pre { white-space: pre-wrap; overflow-wrap: anywhere; }
        button { font: inherit; padding: 0.2rem 0.7rem; cursor: pointer; }
        button:disabled { cursor: default; }
        ul { padding-left: 1.2rem; }
        li { margin: 0.35rem 0; }
        .servers > li { border-top: 1px solid color-mix(in srgb, currentColor 20%, transparent); padding-top: 0.6rem; list-style: none; }
        .servers { padding-left: 0; }
        .warning { color: #b25000; }
        .muted { opacity: 0.75; }
        .note { border-left: 3px solid color-mix(in srgb, currentColor 40%, transparent); padding-left: 0.8rem; }
        .holders > li { list-style: none; margin: 0.6rem 0; }
        .holders { padding-left: 0; }
        .window > p { margin: 0.3rem 0; }
        .window > ul { padding-left: 1.2rem; }
        .countdown { font-variant-numeric: tabular-nums; font-weight: 600; }
        details.entry, p.entry { margin: 0.4rem 0; }
        details.entry > summary { cursor: pointer; }
        #banner { border: 1px solid currentColor; padding: 0.6rem 0.8rem; }
        footer { margin-top: 2.5rem; font-size: 0.9em; }
        """;

    /// <summary>The script.</summary>
    public const string Script = """
        'use strict';
        (() => {
          const body = document.body;
          const main = document.querySelector('main');
          const banner = document.getElementById('banner');
          const say = (text) => { banner.textContent = text; banner.hidden = false; };
          const events = new EventSource('events?tab=' + encodeURIComponent(body.dataset.tab) + '&page=' + encodeURIComponent(body.dataset.page));
          let finished = false;

          events.addEventListener('state', (event) => {
            const open = [...main.querySelectorAll('details[open]')].map((element) => element.textContent);
            const ticked = [...main.querySelectorAll('input[type=checkbox]:checked')].map((element) => element.value);
            main.innerHTML = JSON.parse(event.data).html;
            for (const element of main.querySelectorAll('details')) {
              if (open.includes(element.textContent)) { element.open = true; }
            }
            for (const element of main.querySelectorAll('input[type=checkbox]')) {
              if (ticked.includes(element.value)) { element.checked = true; }
            }
            if (!finished) { banner.hidden = true; }
          });

          events.addEventListener('superseded', () => {
            finished = true;
            events.close();
            window.close();
            say('A newer BrowserAI tab replaced this one, and this one has stopped. You can close it.');
          });

          events.addEventListener('closing', (event) => {
            finished = true;
            events.close();
            say(JSON.parse(event.data).sentence);
          });

          events.onerror = () => {
            if (!finished) { say('BrowserAI is not answering this page. If it does not come back, open BrowserAI from the Start Menu again.'); }
          };

          const pad = (value) => String(value).padStart(2, '0');
          const left = (milliseconds) => {
            const seconds = Math.max(0, Math.ceil(milliseconds / 1000));
            const hours = Math.floor(seconds / 3600);
            const minutes = Math.floor((seconds % 3600) / 60);
            return hours > 0 ? hours + ':' + pad(minutes) + ':' + pad(seconds % 60) : minutes + ':' + pad(seconds % 60);
          };
          setInterval(() => {
            for (const element of main.querySelectorAll('[data-ends-at]')) {
              element.textContent = left(Number(element.dataset.endsAt) - Date.now());
            }
          }, 1000);

          document.addEventListener('click', async (event) => {
            const button = event.target.closest('button[data-action]');
            if (!button || finished) { return; }
            const request = { ...button.dataset };
            button.disabled = true;
            try {
              const response = await fetch('action', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) });
              if (!response.ok) { say('BrowserAI did not take that request.'); }
            } catch {
              say('BrowserAI is not answering this page. If it does not come back, open BrowserAI from the Start Menu again.');
            } finally {
              button.disabled = false;
            }
          });
        })();
        """;
}

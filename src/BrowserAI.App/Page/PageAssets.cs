// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;

namespace BrowserAI.App.Page;

/// <summary>The page's stylesheet and script, served from the page's own origin.</summary>
/// <remarks>
/// <para>
/// <b>Kept in C# and not as files</b>, so the character rule, the wording rule and
/// the licence header reach them through the same scans that read the rest of the
/// product, and so the executable stays one file. Nothing here is inline in the
/// HTML: the content security policy admits scripts and styles from this origin
/// only, and fonts from nowhere, so the faces named are the ones Windows ships.
/// </para>
/// <para>
/// <b>44 a, the maintainer's choice of 2026-10-11</b>: the page follows Windows' light or
/// dark setting through the browser, <c>prefers-color-scheme</c>, and stores nothing.
/// </para>
/// <para>
/// <b>The script does nine things and no more</b>: it keeps one event stream open and
/// replaces the page's main part with each state it is sent, and gives the keyboard focus
/// back to the control that had it, keeps open the folds that were open and the card chosen
/// on the map (46 a); it closes the tab when a newer one has replaced it (Q337 a), falling
/// back to a sentence when the browser will not close it; it shows the last thing the
/// background says before it goes; it posts the action a button names, as JSON, to this
/// origin; once a second it rewrites every countdown from the deadline the element carries,
/// <c>data-ends-at</c>, in milliseconds since 1970; it shows what a card on the map is when
/// the card is chosen; it draws the map's lines from where the cards are (40 a); it writes
/// each new reading of the CPU and memory into the figures and charts that carry its names,
/// <c>data-usage</c> and <c>data-usage-chart</c>, where they stand (59 b); and it folds the
/// after-update notice away when its dismiss is pressed, holding back a new state until the
/// fold has finished (57 a). <i>Corrected 2026-10-12 (previously "seven things"), when the
/// last two arrived.</i>
/// </para>
/// </remarks>
internal static class PageAssets
{
    /// <summary>The narrowest window the conversations page draws its map in: below it, the list.</summary>
    /// <remarks>
    /// <b>Chosen 2026-10-11 for 40 a, against the stylesheet below</b>: the map's five columns at
    /// their least are 672 px, and with the map's own padding, the 360 px inspector beside it,
    /// the gap between them and the page's padding the page needs about 1,150 px. A narrower
    /// window shows the same conversations as a list.
    /// </remarks>
    public const int MapMinimumWidth = 1200;

    /// <summary>The stylesheet.</summary>
    public static string Css { get; } = """
        :root {
          color-scheme: light dark;
          --bg: #f4f6f3; --card: #ffffff; --card-2: #f7f9f7; --line: #e2e7e3; --line-strong: #cbd5cf;
          --text: #17201c; --muted: #56645d; --faint: #8a978f;
          --accent: #087a54; --accent-ink: #ffffff; --accent-soft: #e3f5ec; --accent-text: #08734f;
          --warn: #9a5400; --warn-soft: #fff3dc; --warn-line: #efc98a;
          --bad: #b42318; --bad-ink: #ffffff; --bad-soft: #fde8e6; --bad-line: #f1b3ac;
          --ok: #0f7f54; --seg-a: #10b981; --seg-b: #0e7490;
          --wire: #8a978f; --wire-w: #c26a00; --wire-h: #0f9f6e; --wire-c: #8a978f; --wire-k: #a3b0a8;
          --shadow: 0 1px 2px rgba(23, 32, 28, 0.05), 0 4px 16px rgba(23, 32, 28, 0.05);
          --radius: 16px;
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --bg: #0c1210; --card: #141c19; --card-2: #18221e; --line: #25312c; --line-strong: #34443d;
            --text: #e5ece9; --muted: #9eafa7; --faint: #6f8078;
            --accent: #34d399; --accent-ink: #062b1f; --accent-soft: #11392b; --accent-text: #6ee7b7;
            --warn: #fbbf24; --warn-soft: #3a2a0c; --warn-line: #6b4c14;
            --bad: #f87171; --bad-ink: #2b0b09; --bad-soft: #3b1513; --bad-line: #6e2620;
            --ok: #34d399; --seg-a: #34d399; --seg-b: #22a6c3;
            --wire: #6f8078; --wire-w: #fbbf24; --wire-h: #34d399; --wire-c: #6f8078; --wire-k: #52645c;
            --shadow: 0 1px 2px rgba(0, 0, 0, 0.3), 0 6px 20px rgba(0, 0, 0, 0.25);
          }
        }
        * { box-sizing: border-box; }
        html { background: var(--bg); }
        body { margin: 0; color: var(--text); background: var(--bg); font: 14.5px/1.5 "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif; }
        a { color: var(--accent-text); }
        code, pre { font-family: "Cascadia Mono", Consolas, monospace; font-size: 0.86em; overflow-wrap: anywhere; }
        pre { white-space: pre-wrap; }
        h1, h2, h3, p { margin-top: 0; }
        .icon { width: 18px; height: 18px; flex: none; }
        .brand-mark { width: 30px; height: 30px; flex: none; }
        .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
        :focus-visible { outline: 2px solid var(--accent-text); outline-offset: 2px; }
        main:focus { outline: none; }

        .top { position: sticky; top: 0; z-index: 10; background: color-mix(in srgb, var(--bg) 90%, transparent); backdrop-filter: blur(10px); border-bottom: 1px solid var(--line); }
        .top-in { max-width: 1180px; margin: 0 auto; padding: 12px 32px; display: flex; align-items: center; gap: 14px; }
        .brand { display: flex; align-items: center; gap: 10px; color: var(--text); text-decoration: none; font-weight: 650; font-size: 17px; }
        .version { font-weight: 500; font-size: 12.5px; color: var(--muted); border: 1px solid var(--line-strong); border-radius: 999px; padding: 1px 9px; }
        .top-gap { flex: 1; }
        .running { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; color: var(--muted); }
        .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--ok); box-shadow: 0 0 0 3px color-mix(in srgb, var(--ok) 22%, transparent); }
        .help { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; color: var(--muted); text-decoration: none; padding: 6px 10px; border-radius: 8px; }
        .help:hover { background: var(--card); color: var(--text); }
        .help .icon { width: 15px; height: 15px; }
        .banner { max-width: 1180px; margin: 12px auto 0; padding: 10px 14px; border: 1px solid var(--warn-line); background: var(--warn-soft); border-radius: 12px; }

        main { max-width: 1180px; margin: 0 auto; padding: 28px 32px 56px; }
        body[data-page="sessions"] main, body[data-page="sessions"] .top-in, body[data-page="sessions"] .foot { max-width: 1440px; }
        .crumbs { display: flex; align-items: center; gap: 6px; font-size: 13px; color: var(--muted); margin: 0 0 14px; flex-wrap: wrap; }
        .crumbs a { color: var(--muted); }
        .crumbs .icon { width: 13px; height: 13px; }
        .hello h1, .page-title { font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif; font-size: 30px; line-height: 1.2; font-weight: 650; margin: 0 0 6px; }
        .lede { margin: 0 0 22px; color: var(--muted); font-size: 15.5px; max-width: 780px; }

        .notice, .note { display: flex; gap: 14px; align-items: flex-start; border-radius: 14px; padding: 14px 16px; margin: 0 0 14px; border: 1px solid var(--line-strong); background: var(--card); }
        .notice > .icon, .note > .icon { width: 22px; height: 22px; margin-top: 1px; }
        .notice h3 { margin: 0 0 2px; font-size: 15px; }
        .notice p, .note p { margin: 0 0 4px; }
        .grow { flex: 1; min-width: 0; }
        .notice.bad, .note.broken { background: var(--bad-soft); border-color: var(--bad-line); }
        .notice.bad > .icon, .notice.bad h3, .note.broken > .icon, .note.broken strong { color: var(--bad); }
        .notice.warn { background: var(--warn-soft); border-color: var(--warn-line); }
        .notice.warn > .icon, .notice.warn h3 { color: var(--warn); }
        .notice.good { background: var(--accent-soft); border-color: color-mix(in srgb, var(--accent) 35%, transparent); }
        .notice.good > .icon, .notice.good h3 { color: var(--accent-text); }
        .note.said > .icon { color: var(--accent-text); }

        .grid { display: grid; grid-template-columns: repeat(12, 1fr); gap: 18px; }
        .span-8 { grid-column: span 8; }
        .span-4 { grid-column: span 4; }
        .span-12 { grid-column: span 12; }
        .card { background: var(--card); border: 1px solid var(--line); border-radius: var(--radius); box-shadow: var(--shadow); padding: 20px 22px; min-width: 0; }
        .card-head { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
        .card-head h2 { font-size: 16px; margin: 0; font-weight: 650; }
        .card-head .icon { color: var(--accent-text); }
        .card-head .more { margin-left: auto; font-size: 13px; text-decoration: none; display: inline-flex; align-items: center; gap: 2px; font-weight: 600; }
        .card-head .more .icon { width: 15px; height: 15px; }
        .caption { margin: 0 0 14px; color: var(--muted); font-size: 13.5px; }
        .big { font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif; font-size: 24px; font-weight: 650; margin: 6px 0; }
        .small { font-size: 13px; color: var(--muted); }
        .warning { color: var(--warn); }
        .spaced { margin-top: 18px; }

        .stats { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; margin: 0 0 14px; }
        .stat { background: var(--card-2); border: 1px solid var(--line); border-radius: 12px; padding: 10px 12px; display: flex; gap: 10px; align-items: center; }
        .stat .icon { width: 20px; height: 20px; color: var(--muted); }
        .stat b { display: block; font-size: 20px; line-height: 1.1; }
        .stat span { font-size: 12.5px; color: var(--muted); white-space: nowrap; }
        .stat.warn .icon, .stat.warn b { color: var(--warn); }

        .rows { display: flex; flex-direction: column; border: 1px solid var(--line); border-radius: 12px; overflow: hidden; }
        .row { display: flex; align-items: center; gap: 12px; padding: 11px 14px; text-decoration: none; color: inherit; border-top: 1px solid var(--line); background: var(--card); }
        .row:first-child { border-top: 0; }
        .row:hover { background: var(--card-2); }
        .row-main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
        .row-title { font-weight: 600; overflow-wrap: anywhere; }
        .row-sub { font-size: 12.5px; color: var(--muted); display: flex; align-items: center; gap: 7px; flex-wrap: wrap; margin: 0; }
        .sep { width: 3px; height: 3px; border-radius: 50%; background: currentColor; opacity: 0.6; display: inline-block; }
        .row-side { display: flex; gap: 6px; flex-wrap: wrap; justify-content: flex-end; }
        .row > .icon:last-child { color: var(--faint); width: 16px; height: 16px; }
        .more-line { margin: 10px 0 0; }

        .app { width: 30px; height: 30px; border-radius: 9px; display: inline-grid; place-items: center; font-weight: 700; font-size: 11.5px; flex: none; color: #ffffff; }
        .app.cc { background: linear-gradient(135deg, #7c5ce8, #5b3fc4); }
        .app.cx { background: linear-gradient(135deg, #3f86d0, #2a62a8); }
        .app.other, .app.kept { background: var(--card-2); color: var(--muted); border: 1px dashed var(--line-strong); }
        .app .icon { width: 16px; height: 16px; }

        .chip { display: inline-flex; align-items: center; gap: 6px; font-size: 12.5px; font-weight: 550; border-radius: 999px; padding: 3px 10px 3px 8px; white-space: nowrap; border: 1px solid var(--line); background: var(--card-2); color: var(--muted); }
        .chip .icon { width: 14px; height: 14px; }
        .chip.window { background: var(--warn-soft); border-color: var(--warn-line); color: var(--warn); }
        .chip.hidden { background: var(--accent-soft); border-color: color-mix(in srgb, var(--accent) 30%, transparent); color: var(--accent-text); }
        .chip.live { background: var(--accent); border-color: var(--accent); color: var(--accent-ink); }
        .countdown { font-variant-numeric: tabular-nums; font-weight: 650; }
        .pulse { width: 7px; height: 7px; border-radius: 50%; background: currentColor; animation: pulse 1.6s ease-in-out infinite; display: inline-block; }
        @keyframes pulse { 50% { opacity: 0.25; } }
        .working { display: inline-flex; align-items: center; gap: 6px; color: var(--accent-text); font-weight: 600; }

        .btn { display: inline-flex; align-items: center; gap: 8px; font: inherit; font-weight: 600; font-size: 13.5px; border-radius: 10px; padding: 8px 14px; border: 1px solid var(--line-strong); background: var(--card); color: var(--text); text-decoration: none; cursor: pointer; }
        .btn:hover { border-color: var(--accent-text); }
        .btn .icon { width: 16px; height: 16px; }
        .btn.primary { background: var(--accent); border-color: var(--accent); color: var(--accent-ink); }
        .btn.danger { background: var(--bad); border-color: var(--bad); color: var(--bad-ink); }
        .btn.quiet { border-color: transparent; background: transparent; color: var(--accent-text); padding-left: 6px; padding-right: 6px; }
        .btn:disabled { opacity: 0.55; cursor: default; }
        .btn-row { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 14px; }
        .notice > .btn, .notice > a.btn { align-self: center; }

        .app-rows { display: flex; flex-direction: column; gap: 12px; margin: 6px 0 4px; }
        .app-row { display: flex; gap: 12px; align-items: center; }
        .app-row b { display: block; font-size: 14.5px; }
        .state { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; }
        .state .icon { width: 16px; height: 16px; }
        .state.ok { color: var(--ok); }
        .state.warn { color: var(--warn); }
        .state.missing { color: var(--muted); }

        .minis { display: flex; flex-direction: column; gap: 2px; margin: 12px 0 2px; border-top: 1px solid var(--line); padding-top: 10px; }
        .mini { display: grid; grid-template-columns: 26px 1fr auto; gap: 8px; align-items: center; font-size: 13.5px; padding: 4px 0; }
        .mini-ic { width: 26px; height: 26px; border-radius: 8px; display: grid; place-items: center; background: var(--card-2); color: var(--muted); }
        .mini-ic .icon { width: 15px; height: 15px; }
        .mini-ic.window { background: var(--warn-soft); color: var(--warn); }
        .mini-ic.hidden { background: var(--accent-soft); color: var(--accent-text); }

        .bar { display: block; width: 100%; height: 10px; border-radius: 999px; overflow: hidden; background: var(--card-2); margin: 12px 0; }
        .seg-a { fill: var(--seg-a); }
        .seg-b { fill: var(--seg-b); }
        .legend { display: flex; flex-direction: column; gap: 6px; font-size: 13px; }
        .legend div { display: flex; align-items: center; gap: 8px; }
        .legend span:nth-child(2) { flex: 1; color: var(--muted); }
        .swatch { width: 10px; height: 10px; border-radius: 3px; display: inline-block; }
        .swatch.a { background: var(--seg-a); }
        .swatch.b { background: var(--seg-b); }

        .facts, .kvs { display: grid; grid-template-columns: auto 1fr; gap: 6px 14px; margin: 6px 0 0; font-size: 13.5px; }
        .facts dt, .kvs dt { color: var(--muted); }
        .facts dd, .kvs dd { margin: 0; min-width: 0; overflow-wrap: anywhere; }

        .empty { text-align: center; padding: 26px 18px; border: 1px dashed var(--line-strong); border-radius: 12px; background: var(--card-2); }
        .empty .icon { width: 30px; height: 30px; color: var(--faint); }
        .empty-title { font-weight: 650; margin: 8px 0 4px; }
        .empty p { margin: 0 auto; max-width: 480px; color: var(--muted); font-size: 13.5px; }

        .explain { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; margin: 0 0 22px; }
        .explain > div { background: var(--card); border: 1px solid var(--line); border-radius: 12px; padding: 12px 14px; display: flex; gap: 10px; box-shadow: var(--shadow); }
        .explain .icon { width: 22px; height: 22px; color: var(--accent-text); margin-top: 2px; }
        .explain b { display: block; margin-bottom: 2px; }
        .explain p { margin: 0; font-size: 13px; color: var(--muted); }

        .legend-row { display: none; gap: 16px; flex-wrap: wrap; font-size: 12.5px; color: var(--muted); margin: 0 0 12px; }
        .legend-row span { display: inline-flex; align-items: center; gap: 7px; }
        .legend-row i { width: 24px; height: 0; border-top: 2px solid var(--wire); display: inline-block; }
        .legend-row i.w { border-top: 3px solid var(--wire-w); }
        .legend-row i.h { border-top: 2px dashed var(--wire-h); }
        .legend-row i.c { border-top: 2px dashed var(--wire-c); }
        .legend-row i.k { border-top: 2px dotted var(--wire-k); }

        .map-layout { display: none; grid-template-columns: minmax(0, 1fr) 360px; gap: 18px; align-items: start; margin-bottom: 22px; }
        .map { position: relative; display: grid; grid-template-columns: minmax(220px, 330px) minmax(36px, 1fr) minmax(170px, 220px) minmax(36px, 1fr) minmax(210px, 320px); row-gap: 14px; padding: 20px 20px 24px; border: 1px solid var(--line); border-radius: 20px; background: var(--card-2); }
        .wires { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; overflow: visible; }
        .wire { fill: none; stroke-width: 2; stroke: var(--wire); }
        .wire.w { stroke: var(--wire-w); stroke-width: 3; }
        .wire.h { stroke: var(--wire-h); stroke-dasharray: 9 4; }
        .wire.c, .wire.n { stroke: var(--wire-c); stroke-dasharray: 4 5; }
        .wire.k { stroke: var(--wire-k); stroke-dasharray: 1.5 5; stroke-linecap: round; }
        .wire.live { animation: flow 1s linear infinite; }
        @keyframes flow { to { stroke-dashoffset: -26; } }
        .port { stroke: var(--card-2); stroke-width: 3; fill: var(--wire); }
        .port.w { fill: var(--wire-w); }
        .port.h { fill: var(--wire-h); }
        .col-head { font-size: 11.5px; text-transform: uppercase; letter-spacing: 0.08em; color: var(--muted); font-weight: 650; grid-row: 1; }
        .col-head.c1 { grid-column: 1; }
        .col-head.c3 { grid-column: 3; text-align: center; }
        .col-head.c5 { grid-column: 5; }
        .map > .left { grid-column: 1; display: flex; flex-direction: column; gap: 6px; align-self: center; }
        .map > .right { grid-column: 5; display: flex; flex-direction: column; gap: 8px; align-self: center; }
        .hub-cell { grid-column: 3; display: flex; align-self: stretch; position: relative; z-index: 1; }
        .group-label { font-size: 11.5px; color: var(--muted); display: flex; align-items: center; gap: 6px; margin: 0 0 0 4px; }
        .group-label .icon { width: 13px; height: 13px; }
        .ghost { margin: 0; border: 1px dashed var(--line-strong); border-radius: 14px; padding: 12px 14px; color: var(--muted); font-size: 12.5px; }
        .none { margin: 0; color: var(--muted); font-size: 12.5px; padding-left: 4px; }
        .node { position: relative; z-index: 2; display: flex; gap: 12px; align-items: center; padding: 11px 13px; border-radius: 14px; border: 1px solid var(--line-strong); background: var(--card); box-shadow: var(--shadow); text-align: left; color: inherit; font: inherit; cursor: pointer; width: 100%; }
        .node:hover { border-color: var(--accent-text); }
        .node[aria-pressed="true"] { border-color: var(--accent-text); box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 30%, transparent), var(--shadow); }
        .node .txt { flex: 1; min-width: 0; display: flex; flex-direction: column; }
        .node b { font-size: 14px; font-weight: 600; line-height: 1.3; overflow-wrap: anywhere; }
        .node small { font-size: 12px; color: var(--muted); }
        .node .state { font-size: 12px; font-weight: 600; color: var(--muted); }
        .node.w { border-color: color-mix(in srgb, var(--wire-w) 60%, var(--line-strong)); }
        .node.w .state { color: var(--warn); }
        .node.h .state { color: var(--accent-text); }
        .tag { font-size: 11.5px; padding: 2px 8px; border-radius: 999px; white-space: nowrap; }
        .tag.live { background: var(--accent); color: var(--accent-ink); font-weight: 650; }
        .br-ic { width: 32px; height: 32px; border-radius: 10px; display: grid; place-items: center; flex: none; background: var(--card-2); color: var(--muted); }
        .br-ic.w { background: var(--warn-soft); color: var(--warn); }
        .br-ic.h { background: var(--accent-soft); color: var(--accent-text); }
        .hub { flex-direction: column; justify-content: center; text-align: center; gap: 6px; border-radius: 22px; padding: 18px 14px; border-color: color-mix(in srgb, var(--accent) 50%, var(--line-strong)); background: radial-gradient(120% 70% at 50% 0%, color-mix(in srgb, var(--seg-a) 22%, var(--card)) 0%, var(--card) 70%); }
        .hub .brand-mark { width: 52px; height: 52px; }
        .hub-name { font-weight: 650; font-size: 16px; }
        .hub-stat { display: flex; flex-direction: column; gap: 6px; width: 100%; margin-top: 8px; }
        .hub-row { display: flex; justify-content: space-between; font-size: 12.5px; padding: 6px 10px; border-radius: 10px; background: var(--card-2); }
        .hub-row span { color: var(--muted); }
        .upd { display: block; width: 100%; margin-top: 10px; border-radius: 12px; padding: 9px; font-size: 12.5px; background: var(--warn-soft); color: var(--warn); border: 1px solid var(--warn-line); }
        .upd b { display: block; font-size: 13.5px; }
        .upd.ok { background: var(--accent-soft); color: var(--accent-text); border-color: color-mix(in srgb, var(--accent) 30%, transparent); }
        .r1 { grid-row: 2; } .r2 { grid-row: 3; } .r3 { grid-row: 4; } .r4 { grid-row: 5; }
        .r5 { grid-row: 6; } .r6 { grid-row: 7; } .r7 { grid-row: 8; } .r8 { grid-row: 9; }
        .rows-1 { grid-row: 2 / span 1; } .rows-2 { grid-row: 2 / span 2; } .rows-3 { grid-row: 2 / span 3; } .rows-4 { grid-row: 2 / span 4; }
        .rows-5 { grid-row: 2 / span 5; } .rows-6 { grid-row: 2 / span 6; } .rows-7 { grid-row: 2 / span 7; } .rows-8 { grid-row: 2 / span 8; }

        .inspector { position: sticky; top: 76px; background: var(--card); border: 1px solid var(--line); border-radius: var(--radius); box-shadow: var(--shadow); padding: 18px 20px; max-height: calc(100vh - 96px); overflow: auto; }
        .inspect-none { color: var(--muted); margin: 0; }
        .inspect .kind { font-size: 11.5px; text-transform: uppercase; letter-spacing: 0.08em; color: var(--muted); font-weight: 650; display: flex; align-items: center; gap: 6px; margin: 0 0 4px; }
        .inspect h2 { font-size: 19px; margin: 0 0 10px; overflow-wrap: anywhere; }
        .inspect .what { display: flex; gap: 10px; padding: 11px 13px; border-radius: 12px; background: var(--card-2); color: var(--muted); font-size: 13px; margin: 0 0 12px; }
        .inspect .what p { margin: 0; }
        .inspect .what .icon { color: var(--accent-text); width: 18px; height: 18px; }
        .inspect dl { display: grid; grid-template-columns: 104px 1fr; gap: 7px 12px; margin: 0 0 12px; font-size: 13px; }
        .inspect dt { color: var(--muted); }
        .inspect dd { margin: 0; min-width: 0; overflow-wrap: anywhere; }
        .small-notice { padding: 10px 12px; font-size: 13px; }
        .plain { list-style: none; padding: 0; margin: 0 0 8px; }
        .plain li { margin: 3px 0; }

        .group { margin: 0 0 22px; }
        .group-head { display: flex; align-items: center; gap: 8px; font-size: 13px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--muted); font-weight: 650; margin: 0 0 10px; }
        .group-head .icon { width: 16px; height: 16px; }
        .convo { padding: 0; overflow: hidden; margin-bottom: 12px; }
        .convo-head { display: flex; gap: 12px; align-items: center; padding: 14px 18px; }
        .convo-head h3 { margin: 0; font-size: 16px; overflow-wrap: anywhere; }
        .convo-head .right { margin-left: auto; }
        .convo-foot { padding: 9px 18px; background: var(--card-2); border-top: 1px solid var(--line); font-size: 12.5px; color: var(--muted); display: flex; gap: 8px; align-items: center; margin: 0; }
        .convo-foot .icon { width: 15px; height: 15px; }
        .sess { display: grid; grid-template-columns: 34px 1fr auto; gap: 12px; align-items: center; padding: 12px 18px; border-top: 1px solid var(--line); }
        .sess p { margin: 0; }
        .sess-ic { width: 34px; height: 34px; border-radius: 10px; display: grid; place-items: center; background: var(--card-2); border: 1px solid var(--line); color: var(--muted); }
        .sess-ic.w { background: var(--warn-soft); border-color: var(--warn-line); color: var(--warn); }
        .sess-ic.h { background: var(--accent-soft); color: var(--accent-text); border-color: transparent; }
        .sess-name { font-weight: 650; }
        .sess-purpose, .sess-line { font-size: 13px; }
        .sess-purpose { color: var(--muted); }
        .sess-actions { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
        .none-yet { padding: 12px 18px; border-top: 1px solid var(--line); color: var(--muted); font-size: 13px; margin: 0; }

        .two { display: grid; grid-template-columns: 1.15fr 1fr; gap: 18px; align-items: start; }
        .box { background: var(--card); border: 1px solid var(--line); border-radius: 14px; padding: 16px 18px; margin-bottom: 16px; }
        .box h2 { font-size: 13px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--muted); margin: 0 0 10px; }
        .box p { margin: 0 0 8px; }
        .kv { display: flex; justify-content: space-between; gap: 12px; padding: 7px 0 0; border-top: 1px solid var(--line); font-size: 13.5px; }
        .kv span { color: var(--muted); }
        .path { display: flex; gap: 10px; align-items: center; justify-content: space-between; background: var(--card-2); border: 1px solid var(--line); border-radius: 10px; padding: 8px 10px; margin: 6px 0 10px; }

        .hero { display: flex; align-items: center; gap: 22px; margin-bottom: 18px; flex-wrap: wrap; }
        .hero .clock { font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif; font-size: 44px; font-weight: 650; font-variant-numeric: tabular-nums; color: var(--accent-text); line-height: 1; }
        .hero .clock .icon { width: 44px; height: 44px; }
        .hero-text { flex: 1; min-width: 240px; }
        .hero-text p { margin: 0; }
        .hero-act { display: flex; flex-direction: column; align-items: flex-end; gap: 6px; max-width: 320px; text-align: right; }
        .hold-list { display: flex; flex-direction: column; }
        .hold-item { display: grid; grid-template-columns: 34px 1fr auto; gap: 12px; align-items: center; padding: 12px 0; border-top: 1px solid var(--line); }
        .hold-item:first-child { border-top: 0; }
        .hold-item .sub { font-size: 12.5px; color: var(--muted); margin: 0; }
        .hold-item .sub.warning { color: var(--warn); }
        .effects { margin: 6px 0 14px; padding-left: 18px; }
        .effects li { margin: 5px 0; }

        .table { width: 100%; border-collapse: collapse; font-size: 13.5px; }
        .table th { text-align: left; font-size: 12px; text-transform: uppercase; letter-spacing: 0.05em; color: var(--muted); font-weight: 650; padding: 8px 10px; border-bottom: 1px solid var(--line); }
        .table td { padding: 10px; border-bottom: 1px solid var(--line); vertical-align: top; }
        .table tr:last-child td { border-bottom: 0; }
        .table .num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
        .table a { text-decoration: none; color: var(--text); }
        .table a:hover { text-decoration: underline; }
        .read-at { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
        .working-line { color: var(--accent-text); font-weight: 600; }

        .app-card { margin-bottom: 16px; }
        .app-card .head { display: flex; gap: 14px; align-items: center; margin-bottom: 10px; flex-wrap: wrap; }
        .app-card .head h2 { margin: 0; font-size: 18px; }
        .app-card .head .state { margin-left: auto; font-weight: 600; }
        .app-card .actions { display: flex; flex-direction: column; gap: 4px; margin-top: 10px; }
        .action { margin: 0; display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
        .sub-block { margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--line); }
        .sub-block h3 { font-size: 14px; margin: 0 0 4px; }
        .sub-block > p { margin: 0 0 6px; color: var(--muted); font-size: 13.5px; }
        details.trouble, details.more-info { margin-top: 12px; }
        details.trouble summary, details.more-info summary { cursor: pointer; color: var(--muted); font-size: 13px; }
        details.trouble pre, details.more-info pre { font-size: 12px; background: var(--card-2); border: 1px solid var(--line); border-radius: 8px; padding: 10px; margin: 8px 0 0; }

        .log-group h2 { font-size: 18px; margin: 22px 0 10px; }
        details.entry, p.entry { background: var(--card); border: 1px solid var(--line); border-radius: 12px; margin: 0 0 8px; }
        p.entry { padding: 12px 16px; }
        details.entry summary { cursor: pointer; padding: 12px 16px; }
        details.entry[open] summary { border-bottom: 1px solid var(--line); }
        details.entry p { margin: 0; padding: 12px 16px 14px 34px; color: var(--muted); }
        .entries h2 { font-size: 18px; margin: 22px 0 10px; }

        .foot { max-width: 1180px; margin: 0 auto; padding: 0 32px 40px; color: var(--muted); font-size: 12.5px; display: flex; gap: 16px; flex-wrap: wrap; }
        .foot a { color: var(--muted); }

        body[data-page="status"] main, body[data-page="status"] .top-in, body[data-page="status"] .foot { max-width: 1440px; }
        body[data-page="status"] main { padding-bottom: 28px; }
        body[data-page="status"] .foot { padding-bottom: 24px; }
        .btn.go .icon { width: 16px; height: 16px; margin-left: -2px; }
        .btn.wide { width: 100%; justify-content: center; padding: 11px 16px; font-size: 14.5px; margin-top: 16px; }
        .dismiss { flex: none; align-self: center; width: 32px; height: 32px; margin: 0 -6px 0 0; display: grid; place-items: center; border-radius: 9px; border: 0; background: transparent; color: var(--muted); cursor: pointer; }
        .dismiss:hover { background: color-mix(in srgb, var(--accent) 16%, transparent); color: var(--text); }
        .dismiss .icon { width: 17px; height: 17px; }
        .fold { display: grid; grid-template-rows: 1fr; transition: grid-template-rows 260ms ease, opacity 200ms ease, margin 260ms ease; }
        .fold > .fold-in { min-height: 0; overflow: hidden; }
        .fold.gone { grid-template-rows: 0fr; opacity: 0; }
        main .fold.gone { margin-top: 0; margin-bottom: 0; }
        .live { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; font-weight: 650; color: var(--accent-text); background: var(--accent-soft); border-radius: 999px; padding: 1px 9px 1px 8px; }
        .card-head .live { margin-left: 4px; }
        .stats.five { grid-template-columns: repeat(5, minmax(0, 1fr)); margin: 0; }
        .stats.five .stat { display: grid; grid-template-columns: auto minmax(0, 1fr); grid-template-areas: "ic num" "label label" "sub sub"; column-gap: 10px; row-gap: 0; align-items: center; align-content: start; padding: 12px 14px; }
        .stats.five .stat > div { display: contents; }
        .stats.five .stat > .icon { grid-area: ic; width: 18px; height: 18px; }
        .stats.five .stat > div > span { grid-area: label; white-space: nowrap; font-size: 13px; line-height: 1.3; margin-top: 4px; }
        .stats.five .stat b { grid-area: num; font-size: 30px; line-height: 1.1; font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif; }
        .stats.five .stat small { grid-area: sub; display: block; white-space: nowrap; margin-top: 2px; }
        .stat small .working, .stat small .working span { color: var(--accent-text); }
        .stat small .working { font-weight: 600; gap: 5px; font-size: 12.5px; }
        .stat small .pulse { width: 6px; height: 6px; }
        .metrics { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
        .metric { background: var(--card-2); border: 1px solid var(--line); border-radius: 12px; padding: 12px 14px; min-width: 0; display: flex; flex-direction: column; }
        .m-top { display: flex; align-items: center; gap: 8px; color: var(--muted); font-size: 13px; }
        .m-top .icon { width: 18px; height: 18px; }
        .m-label { font-weight: 600; }
        .m-value { font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif; font-size: 28px; line-height: 1.15; font-weight: 650; margin: 4px 0 6px; font-variant-numeric: tabular-nums; }
        .m-why { margin: 8px 0 0; font-size: 12.5px; color: var(--muted); line-height: 1.4; }
        .m-parts { display: grid; grid-template-columns: 1fr auto; gap: 2px 10px; margin: 2px 0 0; font-size: 12.5px; }
        .m-parts dt { color: var(--muted); }
        .m-parts dd { margin: 0; text-align: right; font-variant-numeric: tabular-nums; font-weight: 600; }
        .chart { position: relative; display: flex; flex-direction: column; margin: 2px 0 0; padding-top: 15px; min-height: 0; }
        .chart svg { display: block; width: 100%; height: 46px; overflow: visible; }
        .chart.grow { flex: 1 1 auto; min-height: 44px; }
        .chart.grow svg { flex: 1 1 0; height: auto; min-height: 24px; }
        .c-top { position: absolute; right: 0; top: 0; font-size: 11px; line-height: 1; color: var(--faint); font-variant-numeric: tabular-nums; pointer-events: none; }
        .c-grid, .c-base { fill: none; stroke: var(--line); stroke-width: 1; vector-effect: non-scaling-stroke; }
        .c-base { stroke: var(--line-strong); }
        .c-area { fill: var(--accent); opacity: 0.1; }
        .c-line, .c-now { fill: none; stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; vector-effect: non-scaling-stroke; }
        .c-line { stroke: color-mix(in srgb, var(--faint) 82%, var(--text)); }
        .c-now { stroke: var(--accent); }
        .c-ring, .c-dot { fill: none; stroke-linecap: round; vector-effect: non-scaling-stroke; }
        .c-ring { stroke: var(--card-2); stroke-width: 13; }
        .c-dot { stroke: var(--accent); stroke-width: 9; }
        .c-x { display: flex; justify-content: space-between; font-size: 11px; color: var(--faint); margin-top: 3px; }
        .band { display: grid; grid-template-columns: minmax(0, 1fr); gap: 14px 32px; align-items: center; margin: 0 0 22px; }
        .band.two { grid-template-columns: minmax(0, 1fr) minmax(0, 560px); }
        .band .lede { max-width: 900px; margin: 0; }
        .band .notice { margin: 0; }
        .split { display: grid; grid-template-columns: minmax(0, 1fr) minmax(320px, 400px); gap: 18px; align-items: stretch; }
        .col { display: flex; flex-direction: column; gap: 18px; min-width: 0; }
        .col-main #card-usage { flex: 1 1 auto; display: flex; flex-direction: column; }
        .metrics.tall { flex: 1 1 auto; grid-template-columns: minmax(0, 1.3fr) minmax(0, 1fr); grid-template-rows: 1fr auto; gap: 10px; }
        .metrics.tall #m-own .m-parts { order: -1; margin: 0 0 4px; }
        .metrics.tall #m-own .m-top { order: -3; }
        .metrics.tall #m-own .m-value { order: -2; }
        .metrics.tall #m-cpu { grid-row: span 2; }
        .col-side { gap: 16px; }
        .col-side .card { padding: 16px 20px; }
        .col-side .big { font-size: 21px; margin: 4px 0; }
        .col-side .card > p:last-child { margin-bottom: 0; }
        @media (max-width: 1180px) {
          .band, .band.two, .split { grid-template-columns: 1fr; }
          .col-side { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
        }
        @media (max-width: 980px) {
          .stats.five { grid-template-columns: repeat(3, minmax(0, 1fr)); }
          .metrics.tall { grid-template-columns: 1fr; grid-template-rows: none; }
          .metrics.tall #m-cpu { grid-row: auto; }
          .metrics.tall .chart.grow { flex: none; }
          .metrics.tall .chart.grow svg { flex: none; height: 80px; }
        }
        @media (max-width: 560px) {
          .stats.five { grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .col-side { display: flex; }
          .card-head { flex-wrap: wrap; row-gap: 4px; }
          .card-head h2 { white-space: nowrap; }
        }
        @media (prefers-reduced-motion: reduce) {
          .fold { transition: none; }
        }

        @media (min-width: {{WIDTH}}px) {
          .map-layout { display: grid; }
          .legend-row { display: flex; }
          .list-view:not(.crowded) { display: none; }
        }
        @media (max-width: 980px) {
          .span-8, .span-4 { grid-column: span 12; }
          .stats { grid-template-columns: repeat(2, 1fr); }
          .explain, .two { grid-template-columns: 1fr; }
        }
        @media (max-width: 560px) {
          .top-in { padding: 10px 16px; }
          main { padding: 20px 16px 48px; }
          .help span, .running { display: none; }
          .hello h1, .page-title { font-size: 24px; }
          .row-side { display: none; }
          .sess { grid-template-columns: 34px 1fr; }
          .sess-actions { grid-column: 2; }
          .hold-item { grid-template-columns: 34px 1fr; }
          .hold-item > :last-child { grid-column: 2; justify-self: start; }
          .hero-act { align-items: flex-start; text-align: left; }
          .foot { padding: 0 16px 32px; }
        }
        @media (prefers-reduced-motion: reduce) {
          .pulse, .wire.live { animation: none; }
        }
        """.Replace("{{WIDTH}}", MapMinimumWidth.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>The script.</summary>
    public const string Script = """
        'use strict';
        (() => {
          const body = document.body;
          const main = document.getElementById('main');
          const banner = document.getElementById('banner');
          const say = (text) => { banner.textContent = text; banner.hidden = false; };
          const unanswered = body.dataset.unanswered;
          const subject = body.dataset.subject ? '&id=' + encodeURIComponent(body.dataset.subject) : '';
          const events = new EventSource('events?tab=' + encodeURIComponent(body.dataset.tab) + '&page=' + encodeURIComponent(body.dataset.page) + subject);
          const svg = 'http://www.w3.org/2000/svg';
          let finished = false;
          let chosen = null;
          let folding = null;
          let heldBack = null;

          const pad = (value) => String(value).padStart(2, '0');
          const left = (milliseconds) => {
            const seconds = Math.max(0, Math.ceil(milliseconds / 1000));
            const hours = Math.floor(seconds / 3600);
            const minutes = Math.floor((seconds % 3600) / 60);
            return hours > 0 ? hours + ':' + pad(minutes) + ':' + pad(seconds % 60) : minutes + ':' + pad(seconds % 60);
          };
          const tick = () => {
            for (const element of main.querySelectorAll('[data-ends-at]')) {
              element.textContent = left(Number(element.dataset.endsAt) - Date.now());
            }
          };

          const inspect = (key) => {
            chosen = key && main.querySelector('[data-inspect="' + key + '"]') ? key : null;
            for (const panel of main.querySelectorAll('.inspect')) { panel.hidden = panel.id !== 'inspect-' + chosen; }
            const none = document.getElementById('inspect-none');
            if (none) { none.hidden = chosen !== null; }
            for (const node of main.querySelectorAll('[data-inspect]')) { node.setAttribute('aria-pressed', String(node.dataset.inspect === chosen)); }
          };

          const line = (into, x1, y1, x2, y2, kind, live) => {
            const middle = (x1 + x2) / 2;
            const path = document.createElementNS(svg, 'path');
            path.setAttribute('d', 'M' + x1 + ' ' + y1 + ' C' + middle + ' ' + y1 + ' ' + middle + ' ' + y2 + ' ' + x2 + ' ' + y2);
            path.setAttribute('class', 'wire ' + kind + (live ? ' live' : ''));
            into.appendChild(path);
          };
          const port = (into, x, y, kind) => {
            const circle = document.createElementNS(svg, 'circle');
            circle.setAttribute('cx', x); circle.setAttribute('cy', y); circle.setAttribute('r', '4.5');
            circle.setAttribute('class', 'port ' + kind);
            into.appendChild(circle);
          };
          const wires = () => {
            const map = document.getElementById('map');
            const into = document.getElementById('wires');
            if (!map || !into) { return; }
            while (into.firstChild) { into.removeChild(into.firstChild); }
            const box = map.getBoundingClientRect();
            const hub = map.querySelector('.hub');
            if (!hub || box.width === 0) { return; }
            const centre = hub.getBoundingClientRect();
            const hubLeft = centre.left - box.left;
            const hubRight = centre.right - box.left;
            into.setAttribute('viewBox', '0 0 ' + box.width + ' ' + box.height);
            for (const row of map.querySelectorAll('.left[data-row]')) {
              const from = (row.querySelector('.node, .ghost') || row).getBoundingClientRect();
              const y = from.top + from.height / 2 - box.top;
              const live = row.dataset.live === '1';
              if (row.querySelector('.node')) {
                line(into, from.right - box.left, y, hubLeft, y, row.dataset.line, live);
                port(into, hubLeft, y, row.dataset.line);
              }
              const right = map.querySelector('.right[data-row="' + row.dataset.row + '"]');
              if (!right) { continue; }
              const sessions = right.querySelectorAll('.node');
              for (const session of sessions) {
                const to = session.getBoundingClientRect();
                line(into, hubRight, y, to.left - box.left, to.top + to.height / 2 - box.top, session.dataset.line, live);
              }
              const none = right.querySelector('.none');
              if (sessions.length === 0 && none) {
                const to = none.getBoundingClientRect();
                line(into, hubRight, y, to.left - box.left - 6, to.top + to.height / 2 - box.top, 'n', false);
              }
              if (sessions.length > 0 || none) { port(into, hubRight, y, sessions.length > 0 ? sessions[0].dataset.line : 'n'); }
            }
          };

          const draw = (data) => {
            const active = document.activeElement;
            const focused = active && active !== main && main.contains(active) ? active.id : '';
            const open = [...main.querySelectorAll('details[open]')].map((element) => element.id).filter((id) => id);
            main.innerHTML = JSON.parse(data).html;
            for (const id of open) {
              const element = document.getElementById(id);
              if (element && element.tagName === 'DETAILS') { element.open = true; }
            }
            tick();
            inspect(chosen);
            wires();
            if (focused) {
              const again = document.getElementById(focused);
              if (again) { again.focus({ preventScroll: true }); }
            }
            if (!finished) { banner.hidden = true; }
          };

          events.addEventListener('state', (event) => {
            if (folding) { heldBack = event.data; return; }
            draw(event.data);
          });

          events.addEventListener('usage', (event) => {
            const usage = JSON.parse(event.data);
            for (const element of main.querySelectorAll('[data-usage]')) {
              const words = usage.text[element.dataset.usage];
              if (words !== undefined) { element.textContent = words; }
            }
            for (const element of main.querySelectorAll('[data-usage-chart]')) {
              const chart = usage.charts[element.dataset.usageChart];
              if (chart !== undefined) { element.innerHTML = chart; }
            }
          });

          const fold = (button) => {
            const notice = button.closest('.fold');
            if (!notice) { return; }
            notice.classList.add('gone');
            void getComputedStyle(notice).opacity;
            folding = Promise.allSettled(notice.getAnimations().map((animation) => animation.finished)).then(() => {
              folding = null;
              if (heldBack !== null) { const data = heldBack; heldBack = null; draw(data); }
            });
          };

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
            if (!finished) { say(unanswered); }
          };

          setInterval(tick, 1000);

          document.addEventListener('click', async (event) => {
            const node = event.target.closest('[data-inspect]');
            if (node) { inspect(chosen === node.dataset.inspect ? null : node.dataset.inspect); return; }
            const button = event.target.closest('button[data-action]');
            if (!button || finished) { return; }
            if (button.dataset.action === 'dismiss-notice') { fold(button); }
            const request = { ...button.dataset };
            button.disabled = true;
            try {
              const response = await fetch('action', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) });
              if (!response.ok) { say('BrowserAI did not take that request.'); }
            } catch {
              say(unanswered);
            } finally {
              button.disabled = false;
            }
          });

          document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && chosen !== null) { inspect(null); }
          });

          window.addEventListener('resize', wires);
          tick();
          inspect(null);
          wires();
        })();
        """;
}

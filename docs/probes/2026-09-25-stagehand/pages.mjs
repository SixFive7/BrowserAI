// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

export const LOCAL_PAGES = ["blank", "form", "table", "iframes", "shadow"];
export const LIVE_PAGES = [
  ["example.com", "https://example.com/"],
  ["wikipedia-playwright", "https://en.wikipedia.org/wiki/Playwright_(software)"],
  ["hn-front", "https://news.ycombinator.com/"],
  ["github-stagehand", "https://github.com/browserbase/stagehand"],
];
export const LOCAL_REPS = 5;
export const LIVE_REPS = 3;
export function pageList(base) {
  return [
    ...LOCAL_PAGES.map((p) => [p, `${base}/${p}.html`, LOCAL_REPS]),
    ...LIVE_PAGES.map(([n, u]) => [n, u, LIVE_REPS]),
  ];
}

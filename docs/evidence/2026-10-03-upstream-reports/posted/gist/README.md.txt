# Headless Firefox persistent launch hangs in safe mode (Windows)

1. `npm install`
2. `npx playwright-core install firefox`
3. `node repro.js`

`MOZ_SAFE_MODE_RESTART=1` puts Firefox into safe mode through the same check a held Shift key goes through at startup.

Expected: `launched in ... ms` after a couple of seconds.

Actual on Windows: `browserType.launchPersistentContext: Timeout 45000ms exceeded.`

Remove `MOZ_SAFE_MODE_RESTART` from the script and it launches normally.

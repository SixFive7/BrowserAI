# TODO

Central work list for BrowserAI. One file, so nothing survives only as a comment
somewhere.

**What belongs here:** work settled in intent but not yet done.

**What does not:** open design questions and known hazards. Those live in the
README, under [Open design decisions](README.md#open-design-decisions) and the
[hazard index](PLAN.md#hazard-index). An item moves here once the decision
behind it is made.

**Format:** `- [ ] **Title.** Why it matters, then what to actually do.` Carry the
README's provenance convention — a claim about an external source needs the date
and version it was true at.

## At v1 launch

- [x] **Review `.gitignore`.** ✅ Done 2026-08-15, ahead of v1, because the
      Velopack spike produced a real `vpk pack` to check the guesses against.
      Fixed: bare `Setup.exe` never matched (the real name carries the app id and
      channel) → `*-Setup.exe`; added the `releases.*.json` / `assets.*.json` feed
      manifests, which were missing entirely; corrected the payload comment, which
      still described a ~806 MB tree with browsers in it; and `Verify.XunitV3` →
      `Verify.TUnit`, left over from before the framework decision. Kept
      `/payload/`, `/staging/`, `/.staging/` deliberately — vpk emits none of
      them, but the build will need somewhere to assemble a payload, so they are
      reviewed rather than deleted on the theory that they are unused.

      **Still owed at v1:** re-fetch the upstream `VisualStudio.gitignore` half
      wholesale, and settle `.vscode/mcp.json` (upstream PR
      [#4735](https://github.com/github/gitignore/pull/4735), open since
      2025-09-23; if unmerged, add `!.vscode/mcp.json` below the marker). Original
      note follows.

- [ ] ~~**Review `.gitignore`.**~~ *(largely discharged above; the upstream-half
      refresh and the `.vscode/mcp.json` decision remain.)* It was written before any code existed, so the
      project-specific half is predictive rather than observed. Four things to
      check:

    - **Refresh the upstream half wholesale.** Lines 1–429 are
      `github/gitignore`'s `VisualStudio.gitignore` verbatim — blob
      `d5a18deed8813c6c817c9090bf0443d7fad48a9d`, verified identical to upstream
      `main` on 2026-08-14, last changed upstream 2026-04-17 (PR #4269).
      Everything below the marker comment is ours. Re-fetch and replace the top
      half in one paste; never merge it by hand.

    - **`.vscode/mcp.json` is currently ignored**, by the template's `.vscode/*`
      rule, which un-ignores only `settings.json`, `tasks.json`, `launch.json`,
      `extensions.json` and `*.code-snippets`. For a project that *is* an MCP
      server, a workspace registration used for testing would be silently
      untracked. Upstream PR
      [#4735](https://github.com/github/gitignore/pull/4735) proposes fixing
      this and has been open since 2025-09-23; if it has not merged by v1, add
      `!.vscode/mcp.json` below the marker. Note `.mcp.json` at the repository
      root — the file the current setup actually uses — is tracked and
      unaffected.

    - **Replace the guessed paths with the real ones.** `/payload/`,
      `/staging/`, `/.staging/`, `/Releases/`, `/RELEASES`, `Setup.exe` and
      `*-Portable.zip` were inferred from the README's install layout, not
      observed from a build. Check what the build and `vpk` actually emit, keep
      what matches, delete the rest.

    - **Settle the two deferred decisions**, both written into the file as
      comments. First, whether the pinned `@playwright/mcp` tree is committed
      for reproducibility rather than installed into staging at build time — the
      `!/mcp/node_modules/` negation is written out ready to uncomment. Second,
      whether §F's artifact work ever produces a source folder named
      `Artifacts/`, which the template's `artifacts/` rule would swallow on
      case-insensitive Windows.

## Decided 2026-08-14 — encoded 2026-08-15

All three are now proper README sections. Retained here only as a record of what
was decided when; the README is the live copy and the two have diverged where
later measurement overruled the original.

- [x] **First-run browser provisioning** → [PLAN §A → first-run browser provisioning](PLAN.md#first-run-browser-provisioning).
      Changed since: `chrome-headless-shell` is no longer provisioned, and the
      manifest/health-check layer was dropped by decision — the recovery is manual
      and the error text carries it.
- [x] **Instance lifetime** →
      [The session directory is the identity](PLAN.md#the-session-directory-is-the-identity),
      [Lifetime](PLAN.md#lifetime-one-timer-and-reclaim-is-forever),
      [Finding sessions](PLAN.md#finding-sessions-without-a-registry).
      Changed substantially since: the central registry is **dropped**, the bearer
      token is **dropped**, labels are **dropped**, and every expiry timer except
      browser-idle is **dropped**. The directory is the identity, the handle and
      the lock.
- [x] **Three browsers, three collision behaviours** →
      [kb: stray detection](kb/windows/detection.md) and
      [kb: profile fallback](kb/chromium/profiles.md). These
      are measured facts, not design, so they belong in the knowledge base rather
      than the charter.

<details>
<summary>Original 2026-08-14 text, kept for provenance</summary>

- [ ] **Encode: first-run browser provisioning.** BrowserAI does **not** bundle the
      full Chrome for Testing browser — the redistribution position is unresolved
      and the only on-point public statement is adverse (a Google engineer, 2023:
      *"Chrome for Testing is a flavor of Google Chrome, so
      google.com/chrome/terms applies"*, which forbids redistribution). It
      downloads on first run instead, as Playwright itself does. Removes 427 MB of
      the 806 MB payload.

    - **The exact version is pinned for free.** `playwright-core/browsers.json`
      carries the revision and `browserVersion`; the CFT URL is
      `https://cdn.playwright.dev/builds/cft/<browserVersion>/win64/chrome-win64.zip`,
      which 307s to Google's bucket. No "latest" lookup exists anywhere in the
      registry code. Old versions still resolve back to Chrome 115 (Jul 2023),
      though Google documents no retention policy.
    - **Integrity must be ours.** Playwright validates only `content-length`, and
      upstream closed and locked the request for checksums
      ([#39559](https://github.com/microsoft/playwright/issues/39559)). Hash each
      archive at build time into a manifest shipped in the artifact, verify after
      download, delete and fail closed on mismatch. Without this, "exactly the
      bytes we tested" is untrue.
    - **Measured 2026-08-14:** chromium 202.3 MB + shell 119.7 MB + ffmpeg + winldd
      = **323.5 MB down, ~700 MiB on disk**, 20.3 s end to end on a 300 Mbps link.
      Arithmetic for slower links: 4 m 19 s at 10 Mbps, 43 m at 1 Mbps.
    - **Timers:** stall 30 s (Playwright's own `NET_DEFAULT_TIMEOUT`, leave it),
      absolute cap 45 min, extraction cap 10 min, outer deadline 60 min as a crash
      tripwire.
    - **`init` must not block.** Return the handle immediately with
      `browserProvisioning: "downloading"`; browser-needing calls return the
      in-progress error; `browser_get_config` still works. **In-session recovery is
      proven** — the same child navigates successfully once the install lands, no
      restart needed.
    - **Strip upstream's remediation string.** It says `Run npx @playwright/mcp
      install-browser chromium`, which BrowserAI does not ship and which would
      resolve a different package and a different revision. A model will act on it.
    - **The failure is invisible by default.** A missing browser gives
      `initialize` OK, `tools/list` OK, stderr empty, and `isError: true` in a
      success-shaped body. A *partial* install gives `spawn EFTYPE` — and
      Playwright then writes `DEPENDENCIES_VALIDATED` into the corrupt directory,
      suppressing revalidation for 30 days. Check `INSTALLATION_COMPLETE` and the
      manifest hash; Playwright never checks the former at launch.
    - **Environment:** strip `PLAYWRIGHT_DOWNLOAD_HOST` and its three per-browser
      variants (they replace the mirror list with one host and destroy failover);
      pass through `HTTPS_PROXY`/`HTTP_PROXY`/`NO_PROXY`/`ALL_PROXY` and
      **`NODE_EXTRA_CA_CERTS`** (needed under TLS inspection). SOCKS is not
      supported by the download path. Egress needs three hosts:
      `cdn.playwright.dev`, `storage.googleapis.com`,
      `playwright.download.prss.microsoft.com`.
    - Browsers live at `%LocalAppData%\BrowserAI\browsers\`, resolved from
      `VelopackLocator.Current.RootAppDir` — **never** inside `current\`, or every
      update re-downloads 700 MB. With `PLAYWRIGHT_SKIP_BROWSER_GC=1` mandated,
      pruning old revisions becomes BrowserAI's job.

- [ ] **Encode: instance lifetime and the session registry.**

    - **Reclaim is forever, and the registry is on disk.** A torn-down handle stays
      resumable against its recorded config and directory. The durable thing is the
      profile, not the process — measured 2026-08-14, a resume after killing the
      node child preserves cookies, localStorage, IndexedDB, service workers and
      CacheStorage, and loses **only `sessionStorage`**, in ~515 ms.
    - **An explicit `resume` tool** alongside `init`. Its job is *legibility*, not
      enforcement: reclaiming becomes a deliberate act with a visible warning
      instead of a silent resurrection. **The lockfile is what actually prevents
      two agents colliding.** Spec-sanctioned family — SEP-2567 names `destroy_*`
      and `list_*` companions to a creation tool.
    - **`init` REFUSES a directory that already has an instance.** Decided
      2026-08-14, and deliberately *not* idempotent. A silent reuse would let an
      agent believe it created something fresh when it inherited another agent's
      live session — the surprise this design exists to prevent. Instead `init`
      fails with an error naming the existing handle and directing the caller to
      `resume`. **Being made to say "resume" is the point:** it converts an
      accidental collision into a stated intent, and an agent that did not expect
      an existing session now knows one exists. Clean separation follows: `init`
      = "create", `resume` = "reclaim", and neither can be mistaken for the other.
    - **`resume`'s warning text is a security surface**, like `init`'s description.
      It is the only place a model is told that reclaiming may stomp another
      agent's work. Write it with that weight and pin it with a test, as
      `SixFive7/OutlookAI` does for its instructions string.
    - **Version the registry schema from day one**, as a required top-level field.
      It is an on-disk format that outlives releases and will be read by a newer
      BrowserAI than wrote it. Unversioned means the first format change is a field
      migration with no way to detect the old shape — and a registry that cannot be
      read is a machine full of orphaned locks.
    - **Two locks, different scopes.** One machine-wide mutex around each registry
      read-modify-write, held for milliseconds; one per-directory profile lock held
      for the instance's life. Conflating them serialises every session.
      `Global\BrowserAI-{sha256(canonical path)[..32]}`; atomic registry writes
      (temp + rename); `Global\` needs `SeCreateGlobalPrivilege`.
    - **Our own lockfile goes in the profile directory**, `FileShare.None` so the
      OS releases it on death, and it **records its holder** — PID, process
      creation time, handle, session type, BrowserAI version. A stale lock then
      yields *"held by PID 1234 since 14:02, no longer running — reclaiming"*
      instead of a bare refusal.
    - **The registry is free crash recovery.** On startup, reap entries whose
      recorded process is gone. That is §D's "alive-but-orphaned holder" solved as
      a side effect.
    - **Version the registry schema from day one.** It outlives releases and is
      read by a newer BrowserAI than wrote it.
    - **Timers (values still open):** browser-idle ~10 min → `browser_close`,
      keeping the node child (measured 329 → 110 MB, 186 ms to relaunch);
      handle-idle ~60 min → full teardown, release the lock, mark reclaimable;
      teardown budget 15 s graceful then job-object kill, matching the child's own
      `setupExitWatchdog`. A never-used handle has no browser at all (~123 MB).
    - **The client watcher is stdin EOF + an `OpenProcess` handle on the client
      PID.** Never ping-based: `ping` is removed at 2026-07-28.
    - **A server cannot ask the agent anything.** Measured: elicitation reaches the
      *human* via a TUI modal and auto-cancels in ~7 ms under `-p`; nothing at any
      spec revision injects text into a model's context unprompted. The only
      workable variant is prepending a line to the **next** tool result.
    - **Eyes open:** forever + on-disk makes a handle a durable bearer token, a
      deliberate departure from the spec's "bounded lifetime" advice for
      unauthenticated servers. Use ≥128 bits of entropy.

- [ ] **Encode: three browsers, three different collision behaviours.** On a
      profile-lock collision — full Chromium returns a clean *"Browser is already
      in use"*; **`chrome-headless-shell` returns nothing at all** and both
      instances share the profile; and **Firefox puts a native modal dialog on the
      user's desktop** (observed 2026-08-14). Playwright's `isProfileLocked`
      pre-flight is Chromium-shaped, checking `lockfile`. All three matter: the
      second is why BrowserAI's own lock is load-bearing, and the third would be an
      invisible hang in a background MCP server.

</details>

## Resolved 2026-08-14 → 2026-08-15

- [x] **The no-timer proposal, and the registry.** ✅ Adopted in full and encoded.
      One timer only — browser-idle — and **the registry is dropped**: the
      directory is the identity, the handle and the lock, and `lock.json` inside it
      is the authority. Labels are gone with it. See
      [The session directory is the identity](PLAN.md#the-session-directory-is-the-identity)
      and [Lifetime](PLAN.md#lifetime-one-timer-and-reclaim-is-forever).

## Open after 2026-08-15

- [x] **Post-reboot resurrection: mechanism excluded, prevention dropped.** ✅
      Measured 2026-08-15 and encoded in
      [kb: resurrection](kb/chromium/resurrection.md).
      `RegisterApplicationRestart` **was never succeeding** — Playwright's command
      line overshoots the 1023-character limit by 531–807 in every shippable
      configuration, verified on live processes with a registering positive control.
      No lever ships; a test asserting the browser is unregistered replaces it. By
      elimination the resurrection came from the Windows sign-in restore path, which
      is `[UNVERIFIED]` without a reboot — the command-line fingerprint that would
      settle it is recorded in the KB.

- [x] **When does the stray sweep run?** ✅ Settled and encoded as
      [The stray sweep](PLAN.md#the-stray-sweep-and-the-concurrency-it-must-survive).
      Two triggers — BrowserAI startup and a logon scheduled task — each looking
      twice, with twelve races enumerated and a test against each. Detection is
      enumeration rather than inventory lookup
      ([kb: enumeration](kb/windows/detection.md#enumeration-works--and-it-moves-the-safety-boundary)),
      so the sweep and the pointer store are now independent.

- [x] **The four named modes become three plus a modifier.** ✅ Settled and encoded
      2026-08-15 as [Three modes](PLAN.md#three-modes-and-tracing-as-a-modifier),
      with the eight-combination table, the reason rows 3–4 stay closed, and
      discoverability as a hard requirement across four model-facing channels
      generated from one table.

- [x] **"Reap" was the wrong word.** ✅ Encoded 2026-08-15 in §E. Confirmed by
      measurement — 16 runs, 106 processes, 0 survivors — so a dead BrowserAI leaves
      no running children at all. Only a stale lockfile survives, which is a file
      problem. The registry lost its last independent justification here.

- [x] **`winldd`, explained.** ✅ No action required; it is informational. Upstream
      passes `["chrome-win"]` while Chromium extracts to `chrome-win64`, so the
      dependency check is a **permanent no-op for Chromium** (and for
      `chromium-headless-shell`, which extracts to `chrome-headless-shell-win64`).
      Firefox passes `["firefox"]`, the real directory, so it runs — 39 binaries,
      +329 ms, cached 30 days. ✅ The promised line is now written: it is the
      *"one specific thing to watch in the `playwright-core` diff"* paragraph in
      [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md), and the standing check is
      [re-verification row 10](kb/README.md#re-verification-index) — if upstream
      fixes the directory name, Chromium suddenly starts validating 39 binaries on
      cold start, and a one-character upstream fix becomes a latency regression.

- [x] **Label reuse.** ✅ Moot. Labels are gone — the directory is the identity.

- [x] **Never kill by image name.** ✅ Encoded 2026-08-15 as
      [§D → Never by image name](PLAN.md#never-by-image-name), with the
      two-mechanism invariant (job object for the living, path-keyed identification
      for survivors), the forbidden-API list at analyzer-error severity, and the
      measured warning that `--user-data-dir` alone is **not** an ownership signal —
      Discord, VS Code, Signal, Teams, WhatsApp, Steam, ChatGPT and four WebView2
      processes all carry it on this machine.

- [x] **`--output-max-size`.** ✅ Resolved and encoded 2026-08-15. Verified in
      `coreBundle.js`: `defaultConfig` contains only `browser` and `timeouts`, and
      `mergeConfig` filters through `pickDefined`, so **no default is applied at any
      merge stage** — the open half of the question is closed. It runs on every tool
      response, recursively lists the whole output directory, and unlinks
      oldest-first past the threshold, sparing only the current response's writes.
      Never set; env var stripped; retention is the calling agent's decision.

- [x] **First-run download self-healing.** ✅ Decided: stay with Playwright's
      built-in capabilities, no manifest and no health-check layer. The consequence
      is stated plainly in
      [§A](PLAN.md#first-run-browser-provisioning) rather than softened —
      a tree corrupted *after* a successful install never re-downloads, because
      `INSTALLATION_COMPLETE` short-circuits without validating. Recovery is the
      `browserai_reinstall_browser` tool plus error text that names the path.

- [x] **Record why an instance exists.** ✅ `purpose` is a **required** field on
      `init`, appended on `resume`, updatable via `browserai_set_purpose`, stored in
      `lock.json`, and played back on a refused `init`, on `resume` and in `list`.
      Encoded, including the caution that it is a channel between agents and must be
      capped, sanitised and framed as data rather than instruction.

- [x] **Per-`init` browser choice.** ✅ On `init` only; `resume` reads it from
      `lock.json` and refuses it as an argument, because a profile is
      browser-specific. Firefox ships in v1.

- [x] **Firefox's `parent.lock` preflight.** ✅ Encoded 2026-08-15 in PLAN.md as *Firefox: the preflight, and a second detection path* — the preflight is mandatory rather than defence in depth, and detection needs only a different attribution step, because image-path detection already covers Firefox for free. Original note follows.

- [x] ~~**Firefox's `parent.lock` preflight.**~~ The one piece of Firefox support that
      is designed but not yet written into the charter as a requirement. Playwright's
      `isProfileLocked` checks only Chromium's `lockfile`, so without our own
      preflight a collision puts a **native modal on the desktop blocking up to
      3 minutes** — an invisible hang in a background server. Our lock is taken
      before launch, so the ordering already covers it, but coverage-by-ordering
      needs a test that fails if the ordering changes. Firefox also needs its own
      stray detection: no `Chrome_MessageWindow` equivalent, so it is `parent.lock`
      sharing-violation → Restart Manager `RmGetList` for the PID.

## Later

- [x] **Upstream-drift check.** ✅ Done 2026-08-15, but **not** as a scheduled job.
      A [`CLAUDE.md` directive](CLAUDE.md#the-daily-drift-check) runs it at most
      once per working day, recording the result in
      [`drift-check.json`](drift-check.json). A poller is unnecessary here: this
      project is built entirely through an agent, so a session-start rule fires by
      construction — the check happens because the work happens. First run: zero
      drift across all five upstreams. The note below is kept for the reasoning,
      which still applies.

- [ ] ~~**Add a scheduled upstream-drift check.**~~ *(superseded by the above; text
      retained for the Dependabot/Renovate analysis.)* Every dependency floats to
      latest *at build time*, and the marker test in
      [`upstream-review.json`](upstream-review.json) fires when a build happens
      — but nothing makes a build happen. Releases are manual, so a quiet month
      is a month in which upstream can move unobserved. This is the missing half
      of the marker: the marker catches "we are about to ship a version nobody
      reviewed"; this catches "upstream moved while nobody was looking".

    - **Dependabot cannot do this job.** Verified against `dependabot-core`'s
      own test table on 2026-08-14: a NuGet `Version="*"` is rewritten to `*`,
      producing a byte-identical file and therefore no PR. npm `"latest"` is
      skipped outright by a dist-tag guard. Dependabot bumps *declared floors*,
      and this project declares none — so `SixFive7/Jeeves`' stated mechanism
      ("Dependabot keeps the floor rising") does not transfer. Renovate can
      reach it only through a `customManagers` regex rule pointed at
      `upstream-review.json`, and has no NuGet lock-file support
      ([#6610](https://github.com/renovatebot/renovate/issues/6610), open).

    - **A working implementation already exists**, written and executed
      2026-08-14: it resolves all five upstreams from the npm and NuGet
      registries and the Node dist index, compares against
      `upstream-review.json`, and reported 0 drift in ~2 s. Two design points
      worth preserving — the drift issue's open/closed state *is* the drift
      state, so the job closes its own issue once the review lands and an open
      issue proves the review has not happened; and it throws on a marker entry
      with no resolver, so a newly-added upstream cannot be silently unwatched.

    - **Prior art says a poller is not optional.** Debian `debian/watch` +
      `uscan`, Homebrew `livecheck` + 3-hourly autobump, and Nix
      `update-flake-lock` are all the same shape: a declared marker plus a
      scheduled checker. Floating with no lock refresh, no poller and a manual
      release is a combination with no located precedent, because that
      combination contains no detector.

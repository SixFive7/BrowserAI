# TODO

Central work list for BrowserAI. One file, so nothing survives only as a comment
somewhere.

**What belongs here:** work settled in intent but not yet done.

**What does not:** open design questions and known hazards. Those live in the
README, under [Open design decisions](README.md#open-design-decisions) and the
[hazard index](README.md#hazard-index). An item moves here once the decision
behind it is made.

**Format:** `- [ ] **Title.** Why it matters, then what to actually do.` Carry the
README's provenance convention — a claim about an external source needs the date
and version it was true at.

## At v1 launch

- [ ] **Review `.gitignore`.** It was written before any code existed, so the
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

## Decided 2026-08-14, not yet written into the README

Three designs were settled in a research session and are recorded here in full so
they survive. **Each needs to become a proper README section** — they are
specifications, not work items, and they are here only because the session that
produced them ran out of room to write them properly.

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

## Open questions carried into the next session (2026-08-14)

The maintainer proposed a substantial simplification and asked several questions
the session ran out of room to answer properly. **Recorded verbatim in intent so
the next session starts from them, not from scratch.**

- [ ] **The no-timer proposal, and whether the registry is needed at all.**
      Maintainer's design: *no* handle/idle expiry timers. `init` refuses any
      existing directory and redirects to `resume` — **including a cleanly closed
      one**, on the grounds that there is no meaningful difference between a lost
      session and a neatly closed one; both must be resumed, so both should behave
      identically. That leaves exactly **one** timer: browser-idle at 10 min,
      reset by any activity (a tool call, or a `resume` followed by one), closing
      the browser but keeping the node child. The reason a session went idle —
      timeout, explicit close, client disconnect — stops mattering entirely.

      **First reaction: this is better than the two-stage design and should
      probably be adopted.** It removes a whole state machine and the three
      distinct error texts collapse toward one. Two follow-ups the next session
      must resolve rather than assume:

    - **Could the label simply *be* the directory?** Maintainer's suggestion, for
      full transparency. Likely yes, and it would delete the label-reuse question
      (see below) outright.
    - **Is the registry then still needed?** Genuinely open, and the answer may be
      no. If the lockfile lives *in* the profile directory and carries its holder,
      and the directory is the identity, then a central registry may add nothing
      but a synchronisation problem. **Do not carry the registry forward by
      inertia** — make it justify itself or drop it.

## Open after 2026-08-15

- [ ] **Post-reboot resurrection: the sweep is not sufficient as designed.** The
      mechanism is now fully understood — full Chromium calls
      `RegisterApplicationRestart` unconditionally, Windows relaunches it at sign-in
      with `--user-data-dir` intact, and the maintainer's machine has
      `RestartApps = 1` with ARSO at its default, so it happens into a locked
      session before anyone signs in. Detection is solved: an exact-title
      `FindWindowExW(HWND_MESSAGE, NULL, "Chrome_MessageWindow", <our dir>)` lookup
      costs ~60 µs and structurally cannot name a foreign profile.

      **What is not solved is when the sweep runs.** The maintainer's objection is
      correct: BrowserAI may not run for weeks, and may never open a given directory
      again, so a sweep-on-start leaves strays alive indefinitely — which is exactly
      the incident. Options recorded for decision: sweep-on-start only; sweep-on-start
      plus a logon scheduled task; or a resident watcher. A logon task needs a way to
      know where sessions live — the default root covers most, and out-of-root
      directories need a pointer list (a list of paths, holding no state, is not the
      registry that was dropped).

- [x] **The four named modes become three plus a modifier.** ✅ Settled and encoded
      2026-08-15 as [Three modes](README.md#three-modes-and-tracing-as-a-modifier),
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
      +329 ms, cached 30 days. One line belongs in `UPSTREAM-REVIEW.md`: if upstream
      fixes the directory name, Chromium suddenly starts validating 39 binaries on
      cold start, and a one-character upstream fix becomes a latency regression.

- [x] **Label reuse.** ✅ Moot. Labels are gone — the directory is the identity.

- [x] **Never kill by image name.** ✅ Encoded 2026-08-15 as
      [§D → Never by image name](README.md#never-by-image-name), with the
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

- [ ] **First-run download: the requirement is self-healing years later.**
      Maintainer's framing, and it is the right one: a pinned version may need
      downloading *years* after the release that pinned it. That raises questions
      the current design only partly answers — what happens when Google prunes an
      old Chrome-for-Testing build (not observed back to Jul 2023, but undocumented
      and unguaranteed); whether a fallback mirror or a side-load path is needed;
      and how a corrupt or partial tree self-heals without human help. Re-explain
      the mechanism from first principles next session; the current TODO entry
      assumes context the maintainer did not have.

- [ ] **`winldd`, explained.** A small Playwright helper (`PrintDeps.exe`) that
      checks a browser's DLL dependencies are present before launch. It matters
      because measurement showed the validation **actually runs for Firefox** (39
      binaries, +329 ms) and is a **permanent no-op for Chromium** — Playwright
      passes `["chrome-win"]` while the real directory is `chrome-win64`. It is
      gated by a `DEPENDENCIES_VALIDATED` marker with a 30-day revalidation period,
      so for Firefox it is a **recurring monthly cost**, not a one-off.

- [ ] **New: record why an instance exists.** Maintainer's proposal — a reason
      field on `init` and `resume`, plus a tool to update it, stored in the
      registry or in the directory. Played back on a refused `init`, on `resume`,
      or via a dedicated "what was this" call, so an agent meeting an existing
      directory can find out what was going on. Strong idea: it is the missing
      human-readable half of the session index, and it directly serves the
      forked-agent case the `resume` redirect exists for.

- [ ] **New: per-`init` browser choice.** Let the caller pick Chromium or Firefox
      per instance. Measured 2026-08-14, Firefox costs ~2x RAM, ~10x first
      navigate, ~24x idle CPU and ~20x profile disk, and its profile-lock refusal
      is a **native GUI modal blocking up to 3 minutes** on a headless server
      (`isProfileLocked` only checks Chromium's `lockfile`, never Firefox's
      `parent.lock`). So Chromium stays the default on engineering grounds alone —
      but Firefox is *safer* on data integrity, since headless Chromium writes no
      lock at all. If Firefox is offered, BrowserAI must do its own `parent.lock`
      preflight or the modal is reachable.

## Later

- [ ] **Add a scheduled upstream-drift check.** Every dependency floats to
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

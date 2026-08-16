# TODO

Central work list for BrowserAI. One file, so nothing survives only as a comment
somewhere.

**What belongs here:** work settled in intent but not yet done.

**What does not:** open design questions and known hazards. Those live in the
README, under [Open design decisions](README.md#open-design-decisions) and the
[hazard index](plan/hazards.md#hazard-index). An item moves here once the decision
behind it is made.

**Format:** `- [ ] **Title.** Why it matters, then what to actually do.` Carry the
README's provenance convention — a claim about an external source needs the date
and version it was true at.

## At v1 launch

- [ ] **Make the marker entry adjudicate what moved — at the first real bump,
      not before.** [The gate](plan/testing.md#what-the-marker-records) requires
      each `upstream-review.json` entry to gain `snapshots` (per snapshot,
      `unchanged` or an adjudication) and `reverification` (an outcome for every
      *manual* row, by name), with a test asserting the entry is consistent with
      what the build observed. Build-order step 4 built everything else in that
      section and deliberately not this. **At a baseline there is nothing to
      adjudicate**: satisfying such a test today means typing an adjudication of
      no change for four snapshots and an outcome for roughly forty manual rows,
      most of which name code that does not exist before
      [step 12](plan/build-order.md#12-the-session-tools-and-config-generation) —
      a review that did not happen, written to make a suite green, which is the
      one act [the procedure](UPSTREAM-REVIEW.md) exists to forbid. Do it on the
      first bump, when there is something true to write; the marker test fires
      on exactly that event, so nothing is relying on anyone remembering.

- [x] ~~**Make `dotnet test` run the tests again.**~~ ✅ **Withdrawn 2026-08-16,
      the same day it was raised — there was never anything to fix.** The
      finding behind it was a single transient observation written up as a
      standing property of the toolchain. It does not reproduce:
      `dotnet test BrowserAI.slnx` returns **51 passed, exit 0** at `e5f4684`,
      and a fresh `git worktree --detach` of **`b8a6553`** — the exact commit the
      entry named as its proof that *"it is not ours"* — returns **30 passed,
      exit 0**. The claimed consequence was false too: steps 1 and 2 were
      evidenced with `dotnet test` reporting 5 and then 13 passing tests, so it
      is not the case that every done-test's evidence came from the executable.
      Retained struck through rather than deleted, because
      [kb row 54](kb/README.md#re-verification-index) and
      [the kb entry](kb/windows/processes.md#interop-and-the-toolchain) are
      retracted in place for the same reason: a reader who saw the original must
      be able to find the retraction.

- [x] **Answer a frame that fails to parse, instead of only logging it.** ✅ Done
      2026-08-16 at [step 9](plan/build-order.md#9-lossless-passthrough), as
      planned. `JsonLines.TryRecoverRequestId` scans a frame that failed to parse
      for a top-level `id`, left to right, stopping at the first thing it cannot
      read — which succeeds on the common case, because every well-behaved
      encoder writes the id near the front. `JsonLinesTransport` answers
      `-32700` when one is recovered and drops-and-logs when none is, because
      inventing an id would resolve a request the caller never made. **Only on
      the caller's leg:** a response is not a thing one answers, so a `-32700`
      aimed at a child would name a request it has no record of. Written from
      the SDK's *behaviour*, not its code — it is Apache-2.0 and this repository
      is not — and the one difference worth noting is that ours needs
      `MaxDepth = int.MaxValue` for the same reason theirs does, which is a
      property of the problem rather than of their solution. Proven by
      `LosslessPassthroughTests.AFrameThatFailsToParseIsAnsweredRatherThanOnlyLogged`
      and `…WithNoRecoverableIdIsDroppedLoudlyRatherThanAnswered`. The message
      text is transport-level and deliberately not catalogue prose; §H.4 is
      [step 13](plan/build-order.md)'s.

- [ ] **Decide whether relayed notifications need their order preserved.** Found
      2026-08-16 at [step 9](plan/build-order.md#9-lossless-passthrough) and
      recorded rather than fixed. The child→caller progress relay preserves the
      `progressToken` and the params byte for byte, and **does not preserve
      order**: the SDK's message loop dispatches inbound notifications
      fire-and-forget, and two `notifications/progress` written by the double in
      order were observed reaching the caller as 2 then 1
      ([kb](kb/mcp/sdk.md#added-2026-08-16--lossless-passthrough-at-220),
      [row 63a](kb/README.md#re-verification-index)). **It cannot be fixed from a
      notification handler** — the reordering has already happened by the time
      the handler runs — so a fix means the `ITransport` decorator
      [deviation 7](plan/stack.md#nine-places-where-the-sdk-must-be-deviated-from)
      originally described, which sees messages in wire order. Two things to
      settle before writing it: whether `@playwright/mcp` emits progress at all
      (not measured), and whether a caller that renders a jumping progress value
      is a defect worth a component. Step 9's done-test does not ask for
      ordering, and claiming it without asserting it would have been the
      [step-8 lesson](plan/build-order.md#8-the-harness-and-the-fake-child)
      repeated.

- [ ] **Find out why `dotnet test` runs zero tests, or record that it stays
      that way.** Measured 2026-08-16 at
      [step 9](plan/build-order.md#9-lossless-passthrough), **five ways**, and it
      is now stable where it was previously transient: `dotnet test` reports
      *"Zero tests ran"*, exit 5, from both shells, against the solution and the
      project, at the working tree, at `c9d30d4`, **and in a fresh worktree of
      `b8a6553`** — the commit that returned 30 passed hours earlier. The same
      assembly run as `BrowserAI.Tests.exe` runs all 106. Versions are identical
      either side (SDK 10.0.302, .NET 10.0.11, TUnit 1.65.0, MTP 2.3.3 from the
      committed lock), so **nothing in this repository and no package float
      explains it**; the machine moved. MTP's own diagnostic shows the host
      launched with `--server dotnettestcli --dotnet-test-pipe …` and the log
      ending immediately after startup configuration, which points at the
      `dotnet test` ↔ MTP handshake rather than at discovery
      ([kb](kb/windows/processes.md#interop-and-the-toolchain)). **Do not "fix"
      it by removing the MTP runner entry from `global.json`** — TUnit is
      MTP-only and there is no VSTest path to fall back to. Until it is
      understood, done-test evidence comes from the executable and the step that
      relies on it says so.

- [ ] **Capture ILC's raw output and fail the publish if it is non-empty.**
      Build-order step 1 asked for two things and only one of them is a property.
      The property half landed 2026-08-16: `SuppressTrimAnalysisWarnings=false`,
      `TrimmerSingleWarn=false` and `ILLinkTreatWarningsAsErrors=true` in
      `src/BrowserAI/BrowserAI.csproj`, so any `IL2xxx`/`IL3xxx` **warning**
      fails the publish. **That does not cover the case the requirement was
      written for.** ILC reports an always-throwing method as neither a warning
      nor an error — [kb: SDK](kb/mcp/sdk.md#added-2026-08-16--not-part-of-the-2026-08-15-spike)
      records a publish that exited 0, emitted zero warnings, produced an
      artifact, and printed `Method '...' will always throw because: Failed to
      load assembly '...'`. No MSBuild property catches that, because it is not a
      diagnostic; only reading ILC's console output does. The check therefore
      needs a publish wrapper, and there is no build script yet to hang it on —
      so it lands with [step 19](plan/build-order.md), where packaging first
      needs one. Until then
      [re-verification row 27](kb/README.md#re-verification-index) carries it as
      *manual*, which is accurate rather than reassuring.

- [ ] **Write `CHANGELOG.md`. The release refuses without it.** The file does not
      exist. [The pre-release checklist](plan/pre-release.md) refuses a release
      whose changelog section for the version being cut is empty, and refuses the
      version `0.0.0` outright — neither check is enforceable until there is a file
      with a defined shape to be empty *of*. Decide the format; decide whether a
      section is headed by the tag or by the bare version, which differ by a `v`
      now that [versions are derived from tags](README.md#settled-2026-08-16); and
      write entries **as work lands**, not by reconstructing them at release time.
      Reconstruction is exactly the failure the empty-section check exists to
      catch, so a checklist satisfied by fifteen minutes of `git log` archaeology
      has been satisfied in form only.

- [ ] **Decide how a git tag becomes a version string.** Settled 2026-08-16: three
      parts plus a pre-release suffix, auto-incremented, nothing hand-edited,
      because `vpk` rejects four-part versions and the house `base.commitcount`
      convention therefore cannot be carried. The **mechanism** is not settled —
      either a build-time package that reads the repository's tags during restore,
      or derivation in the build script from `git describe`. Whichever is chosen
      must emit a `vpk`-acceptable string for an **untagged** commit as well as a
      tagged one, and that is a test rather than an assumption: the untagged
      build's not-a-release suffix is the whole mechanism behind *never self-update
      from a build that is not a release*, so a mechanism that silently produces
      `0.0.0` on a shallow clone or a tagless CI checkout defeats two checklist
      items at once.

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
      what matches, delete the rest. **Payload half done 2026-08-16** with
      build-order step 3: `/payload/` is real and its layout is now written into
      `.gitignore`; `.links/` matches nothing because playwright-core writes it
      into the browsers root, and is kept only as a backstop; the
      `!/mcp/node_modules/` negation was pointed at a path that does not exist
      and now names `/payload/mcp/node_modules/`. **Still owed:** `/staging/`
      and `/.staging/`, which nothing emits, and the Velopack half, which needs
      [step 19](plan/build-order.md).

    - **Settle the two deferred decisions**, both written into the file as
      comments. First, whether the pinned `@playwright/mcp` tree is committed
      for reproducibility rather than installed into staging at build time — the
      `!/mcp/node_modules/` negation is written out ready to uncomment. Second,
      whether §F's artifact work ever produces a source folder named
      `Artifacts/`, which the template's `artifacts/` rule would swallow on
      case-insensitive Windows.

- [ ] **Set `userDataDir`, so a run's browser profile stops landing in a
      directory BrowserAI does not own.** Build-order step 7 generates only the
      keys that decide *which browser runs*, so no `userDataDir` is set and
      upstream falls back to its own default: one
      `%LOCALAPPDATA%\ms-playwright-mcp\mcp-chrome-for-testing-<hash>\` profile
      per distinct configuration, never cleaned up. **Measured 2026-08-16 on this
      machine: 27 profiles, 193 MB**, some of them spike leftovers and several
      added by that step's own suite. It is not a defect in step 7 — the
      directory is the *session's* at
      [§C](plan/C-sessions.md#the-session-directory-is-the-identity) and the key
      belongs to the full generator at
      [step 12](plan/build-order.md#12-the-session-tools-and-config-generation) —
      but between now and then every suite run adds to the pile, so it is
      recorded rather than left to be discovered. **Note the constraint that
      comes with it:** `validateBrowserConfig` throws on `isolated` together with
      `userDataDir`, so the two can never both be set. Nothing existing needs
      deleting before step 12; whether to sweep the 193 MB is the maintainer's
      call, since it is outside the repository and partly predates this work.

## Decided 2026-08-16 — encoded the same day

Nine decisions from the lesson sweep landed in
[README → Settled 2026-08-16](README.md#settled-2026-08-16): lock scope (`Global\`
only, no `Local\` fallback), lock acquisition never waiting, git detection out of
scope, move-versus-copy on a renamed session directory, logging placement, no
automated checks, git-tag version numbers, the plan's delete-when-complete
lifetime, and fix-forward blocking releases indefinitely. The charter is the live
copy; only the work they create is listed here and under
[At v1 launch](#at-v1-launch).

- [ ] **Give `[STALE]` a row in `kb/README.md`'s conventions table.** Resolved
      2026-08-16 in favour of **keeping** it, and
      [`CLAUDE.md`](CLAUDE.md) now says so; the `kb/` half is owed. Today it is
      defined in prose *below* the table and used by no article, which reads as a
      dead marker — it is not. It is the sanctioned alternative to the one thing
      the whole convention exists to forbid, updating a measurement by reasoning,
      so deleting the definition would leave guessing as the only exit from an owed
      re-check — and [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md) already instructs a
      reviewer to apply it, so deletion would strand a procedure step too. Give it
      a row beside `[FLOATS]`, `[STABLE]`, `[MACHINE]` and
      `[UNVERIFIED]`, meaning *a re-check is owed and has not happened*. That no
      entry carries one is the healthy state, not evidence of disuse.

- [x] **No scheduled anything — a decision, not an omission.** ✅ 2026-08-16: no
      CI, no scheduled job, no git hook. The pre-release checklist is the only gate
      that exists. This permanently closes the struck *"Add a scheduled
      upstream-drift check"* under [Later](#later); the
      [daily drift directive](CLAUDE.md#the-daily-drift-check) is unaffected,
      because it is a rule an agent runs rather than a job something schedules. The
      cost is recorded in the charter rather than softened — the gate works when it
      is invoked, and nothing makes it fire — and the decision is marked for review
      once the product is finished.

## Decided 2026-08-14 — encoded 2026-08-15

All three are now proper README sections. Retained here only as a record of what
was decided when; the README is the live copy and the two have diverged where
later measurement overruled the original.

- [x] **First-run browser provisioning** → [PLAN §A → first-run browser provisioning](plan/A-runtime.md#first-run-browser-provisioning).
      Changed since: `chrome-headless-shell` is no longer provisioned, and the
      manifest/health-check layer was dropped by decision — the recovery is manual
      and the error text carries it.
- [x] **Instance lifetime** →
      [The session directory is the identity](plan/C-sessions.md#the-session-directory-is-the-identity),
      [Lifetime](plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever),
      [Finding sessions](plan/C-sessions.md#finding-sessions-without-a-registry).
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
      [The session directory is the identity](plan/C-sessions.md#the-session-directory-is-the-identity)
      and [Lifetime](plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever).

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
      [The stray sweep](plan/C-sessions.md#the-stray-sweep-and-the-concurrency-it-must-survive).
      Two triggers — BrowserAI startup and a logon scheduled task — each looking
      twice, with twelve races enumerated and a test against each. Detection is
      enumeration rather than inventory lookup
      ([kb: enumeration](kb/windows/detection.md#enumeration-works--and-it-moves-the-safety-boundary)),
      so the sweep and the pointer store are now independent.

- [x] **The four named modes become three plus a modifier.** ✅ Settled and encoded
      2026-08-15 as [Three modes](plan/C-sessions.md#three-modes-and-tracing-as-a-modifier),
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
      [§D → Never by image name](plan/D-locking.md#never-by-image-name), with the
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
      [§A](plan/A-runtime.md#first-run-browser-provisioning) rather than softened —
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

- [x] **Firefox's `parent.lock` preflight.** ✅ Encoded 2026-08-15 as
      [§D → Firefox: the preflight, and a second detection path](plan/D-locking.md#firefox-the-preflight-and-a-second-detection-path)
      — the preflight is mandatory rather than defence in depth, and detection
      needs only a different attribution step, because image-path detection
      already covers Firefox for free. Original note follows.

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

- [ ] **Review the "no automated checks" decision once the product is finished.**
      Taken 2026-08-16 with the cost stated plainly: the
      [pre-release checklist](plan/pre-release.md) is the only gate, it works when
      it is invoked, and nothing makes it fire. That trade is right while both the
      suite and the release cadence are predicted rather than observed — many
      commits without re-running everything, and no hosted CI — and it is
      explicitly marked for review when they are neither. Re-open it against the
      finished product and a real cadence, not against a guess about them.

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

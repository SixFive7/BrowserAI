<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

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

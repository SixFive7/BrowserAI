<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Can a human's login be handed to an unattended headless run?

**Research, 2026-08-19. No decision was taken and nothing was implemented.**
Three parallel read-only investigations: what Playwright can carry, what
BrowserAI's session model costs to change, and whether modes should stay a
closed enum. Recorded here because `.work/` is gitignored and one of the three
wrote no scratch file at all.

**Versions in force:** `@playwright/mcp` 0.0.79 · `playwright-core`
1.63.0-alpha-2026-08-05 · Chrome for Testing 152.0.7977.8 (`chromium-1237`) ·
Firefox 153.0 (`firefox-1539`) · Node v24.19.0 · Windows 11 Pro 26200.

---

## The answer: yes, on exactly one route out of three

Measured by driving upstream's own CLI, both families, both directions, with a
public-HTTPS control. The test site sets an HttpOnly **session** cookie — the
shape a real login returns — plus a persistent cookie, localStorage,
sessionStorage and an IndexedDB record.

| Consume arm | Server saw | persistent cookie | localStorage | IndexedDB |
|---|---|---|---|---|
| isolated + `contextOptions.storageState` | **AUTHENTICATED** | yes | yes | no |
| isolated, no state (control) | anonymous | no | no | no |
| persistent + `contextOptions.storageState` | **anonymous — silent no-op** | no | no | no |
| persistent + `browser_set_storage_state` tool | **AUTHENTICATED** | yes | yes | no |
| persistent on a **copy** of the headed profile | anonymous | yes | yes | yes |
| persistent on the headed profile **in place** | anonymous | yes | yes | yes |

Firefox reproduced every arm identically. Reverse direction (headless capture →
headed consume) is symmetric.

### The inversion, which is the whole finding

**`storageState` carries the session cookie that a profile directory drops, and
drops the IndexedDB that a profile directory carries.** Neither carries
sessionStorage. A login hands back a session cookie, so for the one thing worth
moving, **copying the profile is the weaker route, not the safe one.**

### The timing constraint

`storageState` snapshots a **live context**, not a profile on disk. Reopening
the same profile later and capturing yields the persistent cookie only; the
session cookie is unrecoverable, and localStorage returns only for origins
visited *again* in the new context (`addVisitedOrigin` is an in-memory set,
never persisted). **The capture must happen inside the same live browser session
as the login.** Log in, close, then export gets nothing.

*Not measured: whether a crash plus Chromium session-restore preserves a session
cookie. Only clean shutdown was exercised.*

### Why the config route fails silently

`storageState` is the **only** key present in `BrowserNewContextParams` (32 keys)
and absent from `BrowserTypeLaunchPersistentContextParams` (49). Playwright's
`tObject` validator builds its result by iterating *declared* keys, so an
undeclared key is dropped with no error — while `createPersistentBrowser`
spreads `...contextOptions` straight through, so it looks accepted at every
layer a user can see. `--storage-state` with `--user-data-dir` returns exit 0
and an empty stderr. The CLI help's phrase *"for isolated sessions"* is the
entire warning.

`browser_set_storage_state` works because it travels a different channel method
(`BrowserContextSetStorageStateParams`). **Its cost:** server-side it clears the
cache and **all cookies** before restoring, so it destroys a populated profile.

---

## Three corrections to what this repository says about itself

**1. `storage` is a tool filter, not a persistence switch.**
`BrowserConfiguration.cs:188` sets `userDataDir = <session>\profile` in **all
three** modes and `isolated` is never set anywhere. Upstream consumes
`config.capabilities` in exactly one place — `filteredTools(config)` — and
nowhere in context creation. So an `interactive` session's profile already keeps
the cookies a human typed. `SessionMode.cs:82` — *"a human can type a password
this session will not keep"* — and `README.md`'s **"Stored credentials: No / No
/ Yes"** are claims the code does not make. Corroborated by re-verification row
95, whose cookie-decryption measurement was run against a **`headless`** session.

**2. The advertised tool surface does not vary by mode.** BrowserAI answers
`tools/list` from its own child, launched with `UnionCapabilities` — one static
list, 58 upstream plus 6 authored, identical for every mode
(`SessionPolicyTests.cs:113-118`). The 42-vs-59 figure describes the *session's*
child. Consequence: a `browser_cookie_list` against a `headless` session is
advertised, forwarded, and fails inside the child — **asserted nowhere in the
suite**. Second consequence: a mode change would alter nothing a client sees, so
`notifications/tools/list_changed` is not required for it.

**3. A mode is two lines of code.** `mode.Headed` and `mode.Storage` are read at
`BrowserConfiguration.cs:186` and `:190` and nowhere else. Everywhere else
`mode` is a label. Not mode-dependent: browser family, artifact routing,
containment, the idle timer, the sandbox flag, the sweep.

---

## Modes versus toggles

**It is a two-bit space with one vetoed cell** — 4 coherent combinations, 0
incoherent, 1 excluded. Not a curated subset of a large product space.

The veto's recorded reason (*"the one combination granting full credential
access with no visible signal"*) is the class of argument the 2026-08-18 removal
ruled out, which `TODO.md` item 1 already says cannot be both ways — **and
correction 1 above weakens it further: headless already accumulates credentials
with no visible signal.** The fourth mode would add convenient *export*, not
retention.

**Model-facing budget**, reconstructed from source and validated against the
measured 1,261 for `instructions`: a mode costs **104 characters** in each of
two capped channels. `browserai_init`'s description is 1,755 of 2,048.

| Modes | `init` description | Verdict |
|---:|---:|---|
| 3 today | 1,755 | 293 spare |
| **4** | ~1,859 | **189 spare — fits** |
| 5 | ~1,963 | 85 spare — barely |
| 6 | ~2,067 | overflows |

**Why the enum survives the argument:** it makes the invalid combination
*unrepresentable*, where booleans make it merely *undocumented*. Upstream is pure
booleans and re-implements the property as a runtime validator
(`validateBrowserConfig` throws on `isolated` + `userDataDir`). Every
browser-automation precedent that is pure flags has a **human** consumer editing
a config once; this consumer is a model reading a possibly-truncated string per
session. Kubernetes ran the composable form at scale (PodSecurityPolicy) and
**removed** it in v1.25 for three named profiles.

The no-default asymmetry survives: booleans *can* be `required` in JSON Schema,
but a per-switch refusal cannot talk about the **conjunction**, which is what
*"a security posture nobody decided on"* is a statement about.

Directions considered: keep three · **add a fourth** · two required booleans ·
enum plus a `custom` value that unlocks booleans · re-cut the axes so `mode` is
the window and a separate bound argument is the tool surface.

---

## What the handoff needs, and what nothing can fix

Both ends need the `storage` capability — the source to export, the target to
import. Source = headed+storage = `persistent`, which exists. **Target =
headless+storage = the vetoed cell**, so the fourth mode is the workflow's
precondition rather than a convenience.

**Expiry cannot be detected by any shape.** `storageState` carries no
server-side validity. Every route produces the same event: the agent gets a
snapshot of a login form, and unattended the model will plausibly go looking for
credentials. The cheapest mitigation is provenance — `lock.json` is already
append-only timestamped statements, so recording when a human was last at the
keyboard is one field and makes the staleness legible without claiming to detect
it.

### The untested risk that could invalidate the whole approach

**Chromium's User-Agent differs headed vs headless** — `Chrome/152.0.0.0`
versus **`HeadlessChrome/152.0.0.0`** — same binary, same profile, only
`--headless` differing. `navigator.webdriver` was `false` in both. Firefox's UA
is byte-identical across headedness and `navigator.webdriver` is `true` in both.

Any IdP binding a session to the UA will reject the transfer. Add IP binding,
TLS/JA3 fingerprinting, DPoP and device-bound session credentials — none of
which `storageState` carries. **This was proven against a loopback origin and
`httpbin.io`, not against a real identity provider.**

### `--secrets`, a different answer to the same requirement

Upstream ships a dotenv file where the model passes a secret's **name** to
`browser_type`/`browser_fill_form`, `lookupSecret` substitutes the value, and
`redactSecrets` rewrites it out of responses as `<secret>NAME</secret>`. The
model drives the login without ever seeing the credential, and no handoff is
needed. Upstream's own comment calls it *"a convenience and not a security
feature"*, and this repository has already recorded that `browser_get_config`
does not redact and would emit `config.secrets` in plaintext.

---

## Defects found along the way, none acted on

- **Firefox hangs 180 s on a shared profile directory** where Chromium refuses
  in 5,036 ms with a message naming the cause. Upstream's `isProfileLocked`
  probes `<userDataDir>\lockfile` — **Chromium's** lock file name — while
  Firefox uses `parent.lock`, so the guard never fires and Firefox's own lock
  blocks the juggler handshake until the 180 s launch timeout. The error never
  mentions the profile. Newly reachable because this product now offers Firefox;
  re-verification row 22 covers the Chromium half only.
- **`--caps bogus` is silently accepted**, exit 0, no diagnostic —
  `commaSeparatedList` with no enum validation. The same absence is why
  `--caps storage` works although `cli-help.txt` documents only vision, pdf and
  devtools.
- **`browser_storage_state`'s `filename` resolves against the client's cwd**,
  not `outputDir`. Without it, output lands in `outputDir`.
- **File-access roots restrict where a handoff file may live** — outside them,
  `browser_set_storage_state` refuses unless `allowUnrestrictedFileAccess` is
  set. A state file cannot simply sit wherever the human left it, and it is a
  plaintext bearer credential.
- **Two stale doc comments** (`BrowserConfiguration.cs:92-97`,
  `BrowserProxy.cs:370-376`) claim a call its mode does not permit *"is refused
  at call time instead"*. No such refusal exists since the 2026-08-18 removal.

---

## Open decisions

1. Does the "no visible signal" veto survive the 2026-08-18 removal, or fall
   with it? `TODO.md` item 1 says it cannot be both ways.
2. Modes stay a closed enum, or become toggles.
3. The handoff shape: capture-and-restore, or `--secrets`, or neither.
4. Whether a real identity provider is measured before anything is built.

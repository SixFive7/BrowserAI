<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-23 - where the feed and the package live, and who reads what

Three read-only investigations taken the day the release's asset set was decided
asset by asset. They are the evidence behind two decisions and one non-decision:
[what a release publishes](../../../RELEASING.md#what-a-release-publishes), the
[update lane's hosting row](../../../DECISIONS.md#locking-logging-versioning-and-registration),
and the fact that GitHub's automatic *Source code* links cannot be removed at all.

| File | The question | Cited by |
|---|---|---|
| `A-velopack-readers.md` | Every reader of `RELEASES` and `assets.{channel}.json` in Velopack 1.2.158 - writers, readers, and the zero-caller proof | [kb: nothing anywhere reads them from a release](../../../kb/packaging/velopack.md#nothing-anywhere-reads-releases-or-assetschanneljson-from-a-release----measured-2026-09-23), [`RELEASING.md`](../../../RELEASING.md#what-a-release-publishes) |
| `B-pages-hosting.md` | Whether GitHub Pages, or anything else, could host the feed and the package instead of the release page | [`DECISIONS.md`](../../../DECISIONS.md#locking-logging-versioning-and-registration), [kb: the release alias is never cached](../../../kb/packaging/velopack.md#the-release-alias-is-never-cached-and-github-pages-is-cached-for-600-seconds----measured-2026-09-23) |
| `C-source-code-links.md` | Whether the *Source code (zip)* and *(tar.gz)* links on a release can be removed or hidden | [kb: the source-code links cannot be removed](../../../kb/packaging/velopack.md#the-automatic-source-code-links-on-a-release-cannot-be-removed----read-2026-09-23) |

**What was taken from where.** `A` is a shallow clone of
`https://github.com/velopack/velopack` at tag **`1.2.158`**
(sha `3c7f52c1bf17d10ad21b794b006d5ebd1a879a3b`), read file by file; every claim
in it carries a path and a line number. `B` and `C` are `docs.github.com`,
`git-scm.com` and `docs.velopack.io` as they read on 2026-09-23, quoted verbatim,
plus headers measured with `curl -sSI` and counters read with `gh`.

⚠️ **The 479 MB clone is NOT here and is not meant to be.** It was scratch and was
wiped at the session close; what survives is the reading, with the exact tag and
sha so that `git clone --depth 1 --branch 1.2.158 https://github.com/velopack/velopack`
re-creates the same tree. A vendored copy of somebody else's repository is not
evidence, it is a second copy of their repository.

## Two departures from the bytes as written, and both are this tree's rules

**(1) Each file gained the two-line SPDX header** that
`HouseRuleTests.EverySourceFileCarriesTheTwoLineSpdxHeader` requires of every
`.md` here. These are documents meant to be read and rendered, not opaque
artifacts with a published digest, so they keep the `.md` extension and take the
header - unlike
[`../2026-09-23-release-manifest/BrowserAI-1.1.0-release-body.txt`](../2026-09-23-release-manifest/README.md),
where two extra lines would have falsified the one thing that file is for.

**(2) One relative link was re-pointed.** `B` linked
`../../kb/packaging/velopack.md` from `.work/`; from here that is
`../../../kb/packaging/velopack.md`. Nothing else in any of the three moved.

| File | Bytes as written | SHA-256 as written | What changed |
|---|--:|---|---|
| `A-velopack-readers.md` | 9,606 | `4d563ad74f6a7b03d2b28a64c92abbbc6e78b5f203c5570675e5e476e423df32` | header only |
| `B-pages-hosting.md` | 8,562 | `6c608d89c12ed741dcc72a85e632409feb2c6c50e8cf14312c51af592f9b07e1` | header, and one relative link re-pointed |
| `C-source-code-links.md` | 6,345 | `bbef059a16f003ec2c2c02efaf265b10738816f6f146049033b239161b1d0617` | header only |

## What the maintainer decided from them

**Q237 = f: the feed and the full package stay release assets.** None of the five
alternatives `B` sets out was taken; the row in
[`DECISIONS.md`](../../../DECISIONS.md#locking-logging-versioning-and-registration)
names each of them and what it rests on. **Nothing here is a decision by itself** -
this directory is what the decision was read from.

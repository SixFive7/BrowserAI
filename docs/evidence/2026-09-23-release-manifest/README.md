<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-23 - the resolved set `1.1.0` was cut from

[`RELEASING.md` item 11](../../../RELEASING.md#11-the-resolved-set-is-recorded-beside-the-artifact)
says an artifact that cannot state exactly what went into it is not releasable, and
[`build/Write-ReleaseManifest.ps1`](../../../build/Write-ReleaseManifest.ps1) emits this
directory beside the archived `.nupkg`. **This is a copy of the one it emitted for
`1.1.0`**, at `Releases/archive/BrowserAI-1.1.0-manifest/`.

⚠️ **It is here because `Releases/` is gitignored and the release asset was its only
public copy.** Until 2026-09-23 the resolved set reached a reader exactly one way - a
`BrowserAI-1.1.0-manifest.zip` uploaded beside the installer - so a release whose assets
were ever trimmed, or a clone of this repository on its own, could not answer *what went
into this build*. The maintainer's decision on that asset, verbatim, is **"7 move it"**:
the manifest leaves the upload set and is committed here per release instead. **This is a
post-release commit and the tag did not move**: `git rev-list -n1 v1.1.0` is `d3aabf1`
before and after.

| File | What it is |
|---|---|
| `manifest.json` | The version, the tag, the package's SHA-256, the `override` and `pulledForward` keys, and the resolved version every copied file states |
| `src-BrowserAI.packages.lock.json` | `src/BrowserAI/packages.lock.json` |
| `tests-BrowserAI.Tests.packages.lock.json` | `tests/BrowserAI.Tests/packages.lock.json` |
| `tests-BrowserAI.TestProbe.packages.lock.json` | `tests/BrowserAI.TestProbe/packages.lock.json` |
| `payload.package-lock.json` | `build/payload/package-lock.json`, the committed provenance stamp |
| `payload.package.json` | `build/payload/package.json`, the only record that an npm `overrides` entry is in force |
| `payload.json` | `payload/payload.json` - Node's version, LTS name, archive SHA-256 and both tree sizes |
| `browsers.json` | `upstream-snapshots/browsers.json`, the browser revisions from the resolved payload |
| `tool-verdicts.json` | The repository root's, and the `judgedAgainst` upstream versions the verdicts were made on |
| `BrowserAI-1.1.0-release-body.txt` | The release body generated from the `1.1.0` section at the tag. Same bytes as [`../2026-09-23-release-body/body-1.1.0.txt`](../2026-09-23-release-body/README.md), SHA-256 `d9c0f865bbab4b10da6bcacbf8abe42b140bba7506dc7deee6915c0856f30fb6` |

| | |
|---|---|
| Version | `1.1.0`, tag `v1.1.0-0-gd3aabf1`, commit `d3aabf1` |
| Package | `BrowserAI.app-1.1.0-full.nupkg`, **55,022,716** bytes, SHA-256 `438d0d7b153aaa2dcef74375d85a63ba21c4b5b59c6a802c55848ad2ae2ef621` |
| `override` / `pulledForward` | **both `null`** - this release took the newest resolve and held nothing back, and shipped nothing ahead of what the payload's packages declare |
| Written | `2026-09-23T11:30:36.1357756Z` |
| The asset this replaces | `BrowserAI-1.1.0-manifest.zip`, **20,218** bytes, SHA-256 `d492ee2915e7a54a29e6bda0739cc02c45b4bad17a3d3c42f09aaafe0220281e`, still on the published release |

## Two departures from the bytes as emitted, both of them the convention's

**(1) The release body is `.txt` and was emitted as `.md`.** Every `.md` in this tree
carries a two-line SPDX header and `HouseRuleTests.EverySourceFileCarriesTheTwoLineSpdxHeader`
enforces it, so keeping the extension would mean adding two lines to a file whose whole
value is that it is byte for byte what was published. The extension changed and not one
byte did; the sibling directory took the same way out for the same file on the same day,
and its README says so too.

**(2) Five files were emitted with CRLF and are stored with LF**, because
[`.gitattributes`](../../../.gitattributes) normalises line endings for everything this
repository tracks - which [`docs/evidence/README.md`](../README.md) names as one of the
two deliberate departures for every batch here. **Three of the five become byte-identical
to what this repository already holds**: the LF form of each `packages.lock.json` copy
equals the committed lock file it was copied from, to the byte. The other two,
`manifest.json` and `payload.json`, have no committed original - the first is written by
the release script and the second comes from the gitignored `payload/` tree - so their
as-emitted digests are recorded here and nowhere else.

| File | Emitted bytes | SHA-256 as emitted | Stored bytes | SHA-256 as stored |
|---|--:|---|--:|---|
| `manifest.json` | 4,007 | `29c3d19957754471030aeb22ecf3aa5e637e76725d917f77a4c11405566ac40f` | 3,903 | `092bf32f80697924083bb19f6008e0fde52fd4c8cd224d5f5840f8ea7a6324a9` |
| `payload.json` | 808 | `453a5f8faaaf0783eb81d92b47c33f5d9cd17c55fed753c8a0f5d15ad05f472f` | 782 | `15033f02b3156b917d62c347b39d286c5f1b4e65fc0a97442a40fff7caf80810` |
| `src-BrowserAI.packages.lock.json` | 10,379 | `bb8a14a97615724507defda08021fef3fcb4b9ea600f3bfe7d6d657bfce0b4b2` | 10,145 | `096e33839fa3176717e49141489f161b8e1733244c46a92142f08c47a63e1ff1` |
| `tests-BrowserAI.TestProbe.packages.lock.json` | 9,593 | `dfa61efffe7061f90cd5a879f130af7b12ccbcc9c1bb5b3c2d49eec689fde309` | 9,377 | `766e77131af7f96090e89bb1dc96692d10b9a98a2762a8accf32f9f6ed6a0404` |
| `tests-BrowserAI.Tests.packages.lock.json` | 15,608 | `186e948d00848dd81ac9cbf5568553473ae8f75776bde2557a34bedfdd46a7ae` | 15,250 | `5798f9187e0bd5bacd347fb47587b3f7fa61c812148b040fa53fad9298199df8` |

**The other five files are byte-identical either way** and need no row: they were emitted
with LF and are stored with LF.

**How to re-establish it.** The emitted directory is regenerated by
`pwsh -File build/Write-ReleaseManifest.ps1` from the tree at the tag, or by
`build/New-Release.ps1`, which calls it as its eighth step. Comparing this copy against a
fresh one means comparing the LF forms, for the reason above.

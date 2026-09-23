<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Hosting the feed and the package off the release page — the evidence

All GitHub limits quoted from docs.github.com, read 2026-09-23. All headers
measured 2026-09-23 with `curl -sSI`.

## What the installed client actually asks for

`src/BrowserAI.Core/Updates/UpdateConfiguration.cs:63`

```csharp
public const string? ProductionBaseUrl = "https://github.com/SixFive7/BrowserAI/releases/latest/download/";
```

`src/BrowserAI.Core/Updates/UpdateFeed.cs:83` composes
`{BaseUrl}/releases.{Channel}.json`, and
`src/BrowserAI.Core/Updates/VelopackUpdateClient.cs:53-59` hands `feed.BaseUrl`
to `new UpdateManager(string, UpdateOptions)` — which is the `SimpleWebSource`
path. The only runtime lever over it is `BROWSERAI_UPDATE_FEED`
(`UpdateConfiguration.cs:84`, `Resolve` at :96).

**A published release asset cannot be redirected.** There is no GitHub facility
that makes `…/releases/latest/download/releases.win.json` serve from Pages, so
any move strands every already-installed build that has not first updated
through the old URL — unless that install's `BROWSERAI_UPDATE_FEED` is set by
hand.

## The measured install base

`gh release view … --json assets`, 2026-09-23:

| Release | asset | size | downloads |
|---|---|---|---|
| v1.0.0 (2026-09-15) | `releases.win.json` | 260 | **102** |
| v1.0.0 | `BrowserAI.exe` (installer) | 59,453,496 | 2 |
| v1.0.0 | `BrowserAI.app-1.0.0-full.nupkg` | 54,944,824 | 1 |
| v1.0.0 | `assets.win.json` / `RELEASES` / `BrowserAI.zip` / manifest zip | — | 1 each |
| v1.1.0 (2026-09-23T11:50Z) | `releases.win.json` | 260 | 6 |
| v1.1.0 | `BrowserAI.app-1.1.0-full.nupkg` | 55,022,716 | **2** |
| v1.1.0 | `BrowserAI.exe` | 62,585,468 | 0 |
| v1.1.0 | `BrowserAI.zip` / `RELEASES` / `assets.win.json` / manifest zip | — | 0 each |

The `102` against `2` installer downloads is the pattern
[kb/packaging/velopack.md](../../../kb/packaging/velopack.md) records: one installed
BrowserAI polling per server start. **The 1.1.0 package at 2 is new** — every
package asset before today sat at 1. Two readings, and nothing here separates
them: a 1.0.0 install updated itself for the first time, or somebody fetched it
by hand during the cut. Not established.

## GitHub Pages limits (docs.github.com/en/pages/…/github-pages-limits)

Verbatim:

- "Published GitHub Pages sites may be no larger than 1 GB."
- "GitHub Pages sites have a *soft* bandwidth limit of 100 GB per month."
- "GitHub Pages sites have a *soft* limit of 10 builds per hour."
- "GitHub Pages source repositories have a recommended limit of 1 GB."
- "GitHub Pages deployments will timeout if they take longer than 10 minutes."

The page says nothing about cache duration.

At the measured package size (55,022,716 b) the 100 GB/month soft limit is
**~1,860 package downloads per month**. Release assets have no such limit:
docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github
says verbatim *"We don't limit the total size of the binary files in the release
or the bandwidth used to deliver them."*

The 1 GB published-site cap allows **~18 packages live at once**.

## Cache behaviour, measured 2026-09-23

GitHub Pages (`curl -sSI https://pages.github.com/`, and the same on
`https://docs.velopack.io/`, itself a Pages site):

```
Cache-Control: max-age=600
Via: 1.1 varnish
X-Fastly-Request-ID: …
x-proxy-cache: HIT / MISS
```

So a Pages-hosted feed is served from a Fastly edge with a **600-second** TTL —
worst case ten minutes between publishing a release and a client seeing it.

The release alias, by contrast (`curl -sSI` on BrowserAI's own URL, redirect not
followed, so no asset was fetched and no counter moved):

```
HTTP/1.1 302 Found
Location: https://github.com/SixFive7/BrowserAI/releases/download/v1.1.0/releases.win.json
Cache-Control: no-cache
```

**`no-cache` — the alias is never cached, and a new release is visible
immediately.** Moving the feed to Pages is a freshness regression from
"immediate" to "≤10 min". Harmless for correctness (a stale feed just reports
the previous version), but it is a real difference and nothing in this
repository currently depends on it.

## Publishing from a branch, without authoring CI

docs.github.com/en/pages/…/configuring-a-publishing-source-for-your-github-pages-site,
verbatim:

- "You can publish your site when changes are pushed to a specific branch, or you can write a GitHub Actions workflow to publish your site."
- "The source folder can either be the root of the repository (`/`) on the source branch or a `/docs` folder on the source branch."
- ⚠️ "Your GitHub Pages site will always be deployed with a GitHub Actions workflow run, even if you've configured your GitHub Pages site to be built using a different CI tool."

So **no workflow has to be authored**, but a GitHub-owned Actions run
(`pages-build-deployment`) fires on every push to the source branch. Whether that
counts against this project's *"deliberately no hosted CI"* stance is a decision,
not a fact — it is named here rather than resolved.

Current state, read 2026-09-23: `gh api repos/SixFive7/BrowserAI` →
`has_pages: false`, `visibility: public`, `size: 18576` KB (**18.1 MiB**);
`gh api repos/SixFive7/BrowserAI/pages` → 404. Pages is not enabled.

## What committing a 55 MB package per release costs

docs.github.com/…/about-large-files-on-github, verbatim:

- "If you attempt to add or update a file that is larger than 50 MiB, you will receive a warning from Git."
- "GitHub blocks files larger than 100 MiB."
- "We recommend repositories remain small, ideally less than 1 GB, and less than 5 GB is strongly recommended."

A 55 MB `.nupkg` is **over the 50 MiB warning threshold and under the 100 MiB
block**, so it commits — with a warning on every push.

A `.nupkg` is a zip: already compressed, so git delta-compresses successive
versions to approximately nothing. Each release therefore adds ~55 MB
permanently. Against today's 18.1 MiB repository:

| Releases committed | Repo size |
|---|---|
| 1 | ~73 MB |
| 10 | ~570 MB |
| 18 | ~1 GB — the "ideally less than" line, and the Pages source-repo recommendation |
| 90 | ~5 GB — the "strongly recommended" line |

⚠️ **An orphan `gh-pages` branch does not avoid this.** git-scm.com/docs/git-clone,
verbatim on `--single-branch`: *"Clone only the history leading to the tip of a
single branch … Further fetches into the resulting repository will only update
the remote-tracking branch for the branch this option was used for."* — i.e. the
**default** clone fetches every branch's objects. Everyone who clones BrowserAI
pays for every package ever published, unless they pass `--single-branch`,
`--depth`, or `--filter=blob:none`. The only way to keep it out of contributors'
clones is a **different repository**.

## Can a feed entry point at a package on a different host?

**Yes in the C# code, deliberately. No in the documentation.** The two disagree,
and the disagreement is the risk.

Source — `src/lib-csharp/Sources/SimpleWebSource.cs:76-83` @ 1.2.158:

```csharp
// releaseUri can be a relative url (eg. "MyPackage.nupkg") or it can be an
// absolute url (eg. "https://example.com/MyPackage.nupkg"). In the former case
var sourceBaseUri = HttpUtil.EnsureTrailingSlash(BaseUri);

var source = HttpUtil.IsHttpUrl(releaseEntry.FileName)
    ? releaseEntry.FileName
    : HttpUtil.AppendPathToUri(sourceBaseUri, releaseEntry.FileName).ToString();
```

Documentation — docs.velopack.io/distributing/overview, verbatim:

> "You must distribute these packages in the same folder as the
> `releases.{channel}.json` file for updates to work."

and

> "This file should be distributed in the same folder as the `nupkg` files are
> deployed. It contains a list of all available releases. … This file is the only
> way that UpdateManager can discover releases."

So the documented contract is *beside*; the implemented behaviour is *beside or
absolute*. Velopack floats in this repository (`Version="*"`, `BuildConfigurationTests`),
so **relying on the undocumented half is relying on a branch the vendor has not
promised to keep.**

And `vpk` never writes an absolute `FileName`:
`src/lib-csharp/VelopackAsset.cs:84` → `FileName = Path.GetFileName(filePath)`.
Worse, `ReleaseEntryHelper.UpdateReleaseFilesAsync`
(`src/vpk/Velopack.Packaging/ReleaseEntryHelper.cs:100-133`) regenerates
`releases.{channel}.json` from the `.nupkg`s on **every pack**, so any
hand-written absolute URL is silently reverted by the next run of
`build/New-Release.ps1`.

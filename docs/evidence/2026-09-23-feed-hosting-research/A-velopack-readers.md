<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Every reader of `RELEASES` and `assets.{channel}.json` — Velopack 1.2.158

Source of truth: shallow clone of `https://github.com/velopack/velopack` at tag
**`1.2.158`** (sha `3c7f52c1bf17d10ad21b794b006d5ebd1a879a3b`), in
`.work/research-2026-09-23/velopack/`. The tag exists exactly; it is also the
newest tag and matches the default-branch HEAD as of 2026-09-23.
Installed tool `vpk 1.2.158` (`vpk --help` first line); library `Velopack 1.2.158`
in all three `src/*/packages.lock.json`.

## The three file names, and where they come from

| File | Produced by | Name computed at |
|---|---|---|
| `releases.{channel}.json` | `vpk pack`, and every `vpk upload` | `src/lib-csharp/Util/CoreUtil.cs:13-16` — `GetVeloReleaseIndexName` |
| `RELEASES` (channel `win` or empty → no suffix) | `vpk pack` (rewritten each pack), `vpk upload github`, `vpk upload s3/azure/local` | `src/lib-csharp/Util/CoreUtil.cs:18-33` — `GetReleasesFileName`, obsolete at every call site (`#pragma warning disable CS0612/CS0618`) |
| `assets.{channel}.json` | `vpk pack` — `BuildAssets.Write()` | `src/vpk/Velopack.Core/BuildAssets.cs:58-63` |

## WRITERS

| Writer | What it writes | Code |
|---|---|---|
| `vpk pack` post-process | all three | `src/vpk/Velopack.Packaging/PackageBuilder.cs:183-190` → `assetCache.MoveBagTo(releaseDir)`, `assetCache.Write()`, `ReleaseEntryHelper.UpdateReleaseFilesAsync(releaseDir)` |
| `UpdateReleaseFilesAsync` | deletes **every** `RELEASES*` in the dir, then rewrites `RELEASES` and `releases.{channel}.json` from the `.nupkg`s it found | `src/vpk/Velopack.Packaging/ReleaseEntryHelper.cs:100-133` |
| `vpk upload github` | uploads `RELEASES` as a separate asset, only when channel == default Windows channel | `src/vpk/Velopack.Deployment/_GitRelease.cs:219-230` — log text *"Uploading legacy RELEASES (compatibility)"* |
| `vpk upload s3/azure/local` | uploads `RELEASES` under `GetReleasesFileName(channel)` | `src/vpk/Velopack.Deployment/_ObjectStore.cs:225-232` |

`vpk upload` never uploads `assets.{channel}.json` itself — it uploads the files
**listed inside** it (`build.GetFilePaths()`, `_GitRelease.cs:196-204` /
`_ObjectStore.cs:212-214`).

## READERS

| Reader | Reads `RELEASES`? | Reads `assets.{channel}.json`? | Reads `releases.{channel}.json`? | Code |
|---|---|---|---|---|
| C# `SimpleWebSource` — what a bare-URL `UpdateManager` uses, i.e. BrowserAI | **No** | **No** | Yes | `src/lib-csharp/Sources/SimpleWebSource.cs:43` |
| C# `SimpleFileSource` (local dir) | **No** | **No** | Yes; falls back to scanning `*.nupkg` | `src/lib-csharp/Sources/SimpleFileSource.cs:37` |
| C# `GithubSource` / `GitlabSource` / `GiteaSource` via `GitBase` | **No** | **No** | Yes — one per release, merged | `src/lib-csharp/Sources/GitBase.cs:82-108` |
| C# `VelopackFlowSource` | **No** | **No** | Flow API | `src/lib-csharp/Sources/VelopackFlowSource.cs` |
| C# `UpdateManager` check / download / **delta** / **rollback** | **No** | **No** | only through the source above | `src/lib-csharp/UpdateManager.cs:240-315` |
| Rust `lib-rust` `HttpSource` | **No** | **No** | Yes | `src/lib-rust/src/sources/http.rs:39` |
| Rust `lib-rust` `file.rs`, `github.rs`, `gitlab.rs`, `gitea.rs`, `flow.rs` | **No** | **No** | Yes | `src/lib-rust/src/sources/{file,mod}.rs:28,116` |
| Rust `Update.exe` — `apply`, `start`, `patch`, `uninstall`, `update-self` | **No** | **No** | **No** — it never fetches a feed at all | `src/bins/src/update.rs:20-70`; `grep -rn "RELEASES\|releases\.\|get_release_feed\|UpdateSource" src/bins/src/` → zero hits |
| Rust `Setup.exe` / execution stub | **No** | **No** | **No** | same grep |
| `vpk pack` existing-release detection | **No** | **No** | **No** — it enumerates `*.nupkg` on disk | `ReleaseEntryHelper.GetReleasesFromDirAsync`, `src/vpk/Velopack.Packaging/ReleaseEntryHelper.cs:30-42`; the refusal itself at `PackageBuilder.cs:66-73` |
| `vpk upload github` | No | **YES — hard requirement, and it is the LOCAL copy** | No; it *refuses* to merge if the remote release already carries one | `_GitRelease.cs:154` (`BuildAssets.Read`), `_GitRelease.cs:190-194` |
| `vpk upload s3/azure/local` | No | **YES — hard requirement, LOCAL copy** | Yes, the **remote** one, to merge | `_ObjectStore.cs:183`, `_ObjectStore.cs:145-152` |
| `vpk download github/s3/azure/http/local` | **No** | **No** | Yes | `src/vpk/Velopack.Deployment/_DownloadCommandRunner.cs:16-70` |
| `vpk delta` (`DeltaGenCommandRunner`) | **No** | **No** | **No** — two explicit package paths | `src/vpk/Velopack.Packaging/Commands/DeltaGenCommandRunner.cs:18-33` |

### The zero-caller proof

`grep -rn "GetReleasesFileName" --include=*.cs src/` → 4 hits: the definition
(`lib-csharp/Util/CoreUtil.cs:18`), two `vpk` writers, one commented-out block.
**No caller anywhere in `src/lib-csharp` outside the definition** — the client
library cannot read `RELEASES`, because nothing in it ever composes that name.

`grep -rnw "RELEASES" --include=*.rs src/` → **one** hit, and it is a stale doc
comment at `src/lib-rust/src/sources/http.rs:11`. The identical stale sentence is
at `src/lib-csharp/Sources/SimpleWebSource.cs:12`:

> Will perform a request for '{baseUri}/RELEASES' to locate the available packages,

**The code three lines below it calls `CoreUtil.GetVeloReleaseIndexName(channel)`.**
This doc comment is the single most likely reason a reader would believe
`RELEASES` is load-bearing. It is not.

### The `assets.{channel}.json` missing-file failure, verbatim

`src/vpk/Velopack.Core/BuildAssets.cs:65-73`:

```csharp
public static BuildAssets Read(string outputDir, string channel)
{
    var path = Path.Combine(outputDir, $"assets.{channel}.json");
    if (!File.Exists(path)) {
        throw new UserInfoException(
            $"Could not find assets file for channel '{channel}' (looking for '{Path.GetFileName(path)}' in directory '{outputDir}'). " +
            $"If you've just created a Velopack release, verify you're calling this command with the same '--channel' as you did with 'pack'.");
    }
```

## What `RELEASES` is actually for

`ReleaseEntryHelper.cs:115`, verbatim comment:
`// We write a legacy RELEASES file to allow older applications to update to velopack`
and `_GitRelease.cs:228`: `"Uploading legacy RELEASES (compatibility)"`.
`GetLegacyMigrationReleaseFeedString` (`ReleaseEntryHelper.cs:150-164`) emits
**one line, the newest Full release only**, in Squirrel's
`<SHA1> <filename> <size>` format. It is a Squirrel→Velopack migration shim and
nothing else. BrowserAI's copy, byte for byte (83 b, UTF-8 BOM):

```
23FB729B035F39D30FD6E045965593E61DCE0ACC BrowserAI.app-1.1.0-full.nupkg 55022716
```

## What `assets.win.json` actually holds

BrowserAI's copy, verbatim (180 b) — no checksums, no versions, no URLs:

```json
[{"RelativeFileName":"BrowserAI.exe","Type":"Installer"},{"RelativeFileName":"BrowserAI.zip","Type":"Portable"},{"RelativeFileName":"BrowserAI.app-1.1.0-full.nupkg","Type":"Full"}]
```

It is a **hand-off between `vpk pack` and `vpk upload` on one machine**: the list
of files pack produced, so upload knows what to push. Nothing consumes it after
an upload, on any host.

## Absolute URLs in a feed entry — C# deliberate, Rust incidental

`src/lib-csharp/Sources/SimpleWebSource.cs:76-83`, verbatim:

```csharp
// releaseUri can be a relative url (eg. "MyPackage.nupkg") or it can be an
// absolute url (eg. "https://example.com/MyPackage.nupkg"). In the former case
var sourceBaseUri = HttpUtil.EnsureTrailingSlash(BaseUri);

var source = HttpUtil.IsHttpUrl(releaseEntry.FileName)
    ? releaseEntry.FileName
    : HttpUtil.AppendPathToUri(sourceBaseUri, releaseEntry.FileName).ToString();
```

`HttpUtil.IsHttpUrl` (`src/lib-csharp/Util/HttpUtil.cs:22-29`) = parses as an
absolute `Uri` whose scheme is http or https.

Rust: `src/lib-rust/src/sources/http.rs:57` uses `url::Url::join(&asset.FileName)`,
and RFC-3986 reference resolution makes an absolute reference replace the base —
so it works there too, but by the URL crate's semantics rather than an explicit
branch. Not load-bearing for BrowserAI: the C# `UpdateManager` does the download
and hands `Update.exe` a local path.

**Local staging is unaffected by an absolute URL.** `Locator.GetLocalPackagePath`
(`src/lib-csharp/Locators/VelopackLocatorExtensions.cs:15-18`) is
`Path.Combine(PackagesDir, PathUtil.GetSafeFilename(asset.FileName))`, and
`GetSafeFilename` (`src/lib-csharp/Util/PathUtil.cs:65-67`) starts with
`Path.GetFileName`. Measured on this machine, PowerShell 7.6.6:

```
[System.IO.Path]::GetFileName("https://sixfive7.github.io/BrowserAI/feed/BrowserAI.app-1.1.0-full.nupkg")
→ BrowserAI.app-1.1.0-full.nupkg
```

**But `vpk` never writes one.** `VelopackAsset.FromZipPackageNoChecksum`
(`src/lib-csharp/VelopackAsset.cs:84`) sets `FileName = Path.GetFileName(filePath)`
— a bare name. An absolute URL in the feed is therefore a **post-pack rewrite of
`releases.win.json` that nothing in the toolchain performs**, and which
`vpk pack` would silently undo on the next run (`UpdateReleaseFilesAsync`
regenerates the file from the `.nupkg`s every time).

## Query parameters the client appends

`SimpleWebSource.GetReleaseFeed` (`SimpleWebSource.cs:45-60`) appends
`arch`, `os`, `rid` always, and `id` + `localVersion` when a local release is
known. So the request BrowserAI actually issues is
`…/releases.win.json?arch=X64&os=win&rid=win-x64&id=BrowserAI.app&localVersion=1.1.0`.
Harmless against a GitHub release asset; it is a cache-key multiplier against any
CDN that varies on the query string.

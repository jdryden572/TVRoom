# TVRoom docs

Working notes for the TVRoom server: open issues from two code reviews, proposed
enhancements, and larger architecture designs.

## Architecture

| Document | Status |
| --- | --- |
| [architecture-server-agent-split.md](architecture-server-agent-split.md) | Design agreed, not started — split into a central server plus agents deployed near a tuner |

Architecture docs describe a whole direction and its decisions. They are too large
to action directly and carry a decomposition sketch at the end; the resulting
tasks become enhancement docs.

## Known issues

| Document | Scope |
| --- | --- |
| [known-issues.md](known-issues.md) | Repo-wide: build health, dependencies, hosting configuration, hygiene |
| [known-issues-hls.md](known-issues-hls.md) | The HLS ingest/serve pipeline in `TVRoom/HLS/` |

Both files list only issues that are still **open**. Close an entry by deleting
it when the fix lands.

## Enhancements

| Document | Status |
| --- | --- |
| [enhancement-hls-signed-url-auth.md](enhancement-hls-signed-url-auth.md) | Proposed — authenticate the `/streams/*` endpoints with path-embedded signed tokens |
| [enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md) | Proposed — serialize broadcast lifecycle transitions behind a single mailbox loop |
| [enhancement-ffmpeg-progress.md](enhancement-ffmpeg-progress.md) | Proposed — replace stderr scraping with ffmpeg `-progress` for transcode telemetry |
| [enhancement-streaming-seam-types.md](enhancement-streaming-seam-types.md) | Proposed — standardize on `IAsyncEnumerable<T>` at component boundaries; includes a regression to fix before `refactor-for-agents` merges |
| [enhancement-viewer-presence.md](enhancement-viewer-presence.md) | Proposed — show which users are watching a broadcast; depends on the signed-URL task |
| [enhancement-core-extraction.md](enhancement-core-extraction.md) | Proposed — decouple the HLS state machine from ASP.NET so it is testable without a web host; do before the test rewrite |
| [enhancement-multi-broadcast.md](enhancement-multi-broadcast.md) | **Aspirational** — more than one broadcast at a time; not planned, but one schema choice should ride along with earlier work |

Each enhancement doc records the design, the alternatives already rejected and
why, and any prerequisites.

## Already addressed

Findings that have been fixed, and are therefore deliberately absent from the
lists above. Kept as a record of what changed and why, so the reasoning survives
the issue being closed.

- **Google ID token audience was never validated.** `/signin` called
  `GoogleJsonWebSignature.ValidateAsync` without `ValidationSettings`, which
  suppresses audience validation entirely, so any unexpired Google-signed ID
  token was accepted regardless of which OAuth client it was issued to. It now
  validates against `Authentication:Google:ClientId` plus an optional
  `Authentication:Google:ApiClientIds` array for non-web callers.
- **`/transcodeConfig` was reachable by any Viewer.** The endpoint had no
  authorization metadata and fell through to the Viewer fallback policy, while
  its values are interpolated into the ffmpeg command line. Both the GET and
  POST now require `Policies.RequireAdministrator`.
- **No `.gitattributes`, and inconsistent `core.autocrlf`.** The index held LF
  while 116 working-tree files were CRLF, left over from a checkout under a
  different setting — `core.autocrlf` is `true` at system level and `false` at
  global on at least one dev machine. Git's stat cache hid the mismatch until a
  file was touched, at which point a small edit became a whole-file diff.
  `.gitattributes` now pins `* text=auto eol=lf` and the working tree has been
  normalized. **LF in the working tree is deliberate**, not the usual `native`
  default: C# raw string literals inherit their source file's endings, so a CRLF
  checkout would change the bytes the HLS playlist writers serve and make a
  Windows build differ from the Linux Docker build. This also makes the setting
  repo-controlled rather than machine-controlled. Partly addresses issue 4 in
  [known-issues-hls.md](known-issues-hls.md), which stays open for the
  mixed-terminator output itself.

- **The test project did not compile**, broken since `42905cd` (November 2024)
  when `ScopedBufferPool` changed `HlsFileIngester` and `SharedBuffer.Create`
  without updating callers. Fixed in `be1ce5f`: 33 tests build and pass.
  `DisposeAllSegments` was correctly **not** restored — it was removed
  deliberately when the pool took over teardown — so the tests were rewritten
  against the current design rather than the superseded one. They now run in CI
  on every branch push — see the CI entry below.

- **Stale dependencies and eight High-severity advisories.** Everything was
  pinned at `9.0.0` from November 2024. The app and tests now target **.NET 10**
  and every package is at its latest stable version; `dotnet list package
  --vulnerable --include-transitive` reports none, and `--outdated` is empty. This
  closed what was issue 2 in [known-issues.md](known-issues.md). Notable pieces:

  - `Microsoft.CodeAnalysis.NetAnalyzers` was **removed** rather than bumped - the
    SDK ships its own analyzers, and `EnableNETAnalyzers` / `AnalysisLevel` were
    already set. This also cleared the version-mismatch warning.
  - `ForwardedHeadersOptions.KnownNetworks` is obsolete in .NET 10 (`ASPDEPR005`);
    switched to `KnownIPNetworks`.
  - `Vite.AspNetCore` 1.12 -> 2.4.1 dropped the `.Extensions` sub-namespace, and
    moved `PackageDirectory` / `PackageManager` from `Vite:*` onto `Vite:Server:*`.
    Options binding ignores unmatched keys, so the stale `Vite:PackageDirectory`
    in `appsettings.Development.json` failed **silently**: the dev server fell back
    to the project directory, which has no `package.json`, and never launched. The
    compiler caught the namespace half of this break; nothing caught the config
    half. Verified fixed by running the app and watching Vite start on 5173.
  - MSTest 4 removed `[ExpectedException]`; those two tests now use
    `Assert.ThrowsExactly`.
  - Two tests in `HlsFileIngesterTests` subscribed to the hot `StreamSegments`
    observable *after* ingesting, racing the consumer loop. .NET 10 scheduling made
    the loop win consistently, turning a latent flake into a hard failure. Both now
    subscribe up front. The race was pre-existing, not a .NET 10 defect.

  **Behaviour change worth knowing:** cookie-auth challenges on *minimal API*
  endpoints now return **401** instead of a 302 to `/login`. Razor Pages still
  redirect, so the browser login flow is unchanged; only `fetch()` callers see the
  difference, and 401 is the more correct answer there. Verified against a .NET 9
  baseline.

  **Deliberately not bumped:** the `lscr.io/linuxserver/ffmpeg:7.0.2` base image.
  `TranscodeStats.TryParse` scrapes ffmpeg stderr, so a version change risks a
  silent telemetry break - see
  [enhancement-ffmpeg-progress.md](enhancement-ffmpeg-progress.md), which should
  land first.

- **Eight of nine high-severity npm advisories.** `npm audit fix` on
  `TVRoom/client` cleared them by touching **only `package-lock.json`** — every
  fix landed inside the existing semver ranges, so no declared dependency moved.
  Notably this patched `@xmldom/xmldom` (via `video.js` -> `mpd-parser`), the one
  high that actually shipped to the browser, and carried `vite` 5.0.12 -> 5.4.21.
  Verified with `npm run build` and by booting the app and watching the Vite dev
  server start. The ninth high is `vite` itself, which cannot be patched without
  migrating to Svelte 5 — tracked as issue 3 in
  [known-issues.md](known-issues.md).

- **Nothing built or tested the solution in CI.** Two workflows now cover it:

  - `tests.yml` runs `dotnet restore` / `build` / `test` on **every branch
    push**, uploading the `.trx` as an artifact even on failure. It installs Node
    because building `TVRoom.csproj` triggers the `DebugEnsureNodeEnv` target,
    which shells out to `npm install` whenever `client/node_modules` is absent —
    always true on a fresh checkout — even though the tests never touch the front
    end. NuGet and npm caches are keyed so reruns stay fast.
  - `publish-container.yml` builds the Docker image and publishes it to GHCR on
    **every branch push** and on `v*` tags. Only `main` gets `:latest`; other
    branches publish under their own name, sanitized by `docker/metadata-action`
    (`feature/foo` becomes `feature-foo`), and every build additionally gets a
    `sha-` tag. Fork pull requests build without publishing; pull requests from a
    branch in this repo are skipped by a job-level `if`, because the push trigger
    already built that commit. Auth is the default `GITHUB_TOKEN`; there is no
    secret to configure.

    Note that branch tags are **not** garbage-collected when a branch is deleted,
    so GHCR will accumulate one image per feature branch. If that becomes
    annoying, prune with `actions/delete-package-versions` on a schedule.

  This also required adding a **`.dockerignore`**, which did not exist. `COPY . .`
  was copying everything, and the consequential part was `client/node_modules`:
  `DebugEnsureNodeEnv` is conditioned on that directory being *absent*, so a host
  (Windows) `node_modules` copied into a Linux image silently **skips**
  `npm install` and leaves platform-wrong esbuild/rollup binaries. It would also
  have baked `tvroom.db` into a published image.

  Verified that excluding `wwwroot` from the build context is safe: the Web SDK's
  `Content Include="wwwroot/**"` glob is evaluated before any target runs, and
  `wwwroot/` is gitignored, so a clean checkout could plausibly have published an
  image with no JS or CSS. Deleting `wwwroot` and running `dotnet publish
  -c Release` put all 11 assets in the output — .NET's static-web-assets pipeline
  picks them up after `PublishBuildAssets` runs. What is still open is that
  neither workflow passes `-warnaserror`; see issue 4 in
  [known-issues.md](known-issues.md).

## A note on severity labels

Severities describe impact on this deployment: a single-tenant home server
behind a reverse proxy, with a small set of explicitly authorized Google
accounts. They are not generic CVSS-style ratings.

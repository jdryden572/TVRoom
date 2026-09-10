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

Two findings from the first review were fixed and are deliberately absent from
these lists. Both changes are in the working tree and not yet committed:

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
  against the current design rather than the superseded one. What remains open is
  that no CI job runs them; see issue 1 in [known-issues.md](known-issues.md).

## A note on severity labels

Severities describe impact on this deployment: a single-tenant home server
behind a reverse proxy, with a small set of explicitly authorized Google
accounts. They are not generic CVSS-style ratings.

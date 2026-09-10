# Known issues — repo-wide

Open items from the code review of 2026-09-01, minus the two that were fixed
(see [README.md](README.md)). Ordered by how much they matter.

---

## 1. The test project does not compile, and nothing builds it

**Severity:** High (process) · **Area:** `TVRoom.Tests/`

`dotnet test` fails to build. The main project is fine; only the tests are broken:

```
TVRoom.Tests/HLS/HlsFileIngesterTests.cs(19,49):  error CS7036: no argument given for
    required parameter 'bufferPool' of 'HlsFileIngester.HlsFileIngester(ScopedBufferPool)'
TVRoom.Tests/HLS/SharedBufferTests.cs(13,67):     error CS7036: no argument given for
    required parameter 'pool' of 'SharedBuffer.Create(ReadOnlySequence<byte>, ILogger, ScopedBufferPool)'
TVRoom.Tests/HLS/HlsStreamTests.cs(107,18):       error CS1061: 'HlsStreamWithSegments' does not
    contain a definition for 'DisposeAllSegments'
TVRoom.Tests/HLS/HlsFileIngesterTests.cs(132,33): error CS7036: (same as above)
TVRoom.Tests/HLS/HlsStreamTests.cs(213,33):       error CS7036: (same as above)
```

Commit `42905cd` (2024-11-11) introduced `ScopedBufferPool` and changed those
signatures without updating callers in the test project. The only workflow in
`.github/workflows/` deploys the cast receiver to GitHub Pages — nothing builds
or tests the .NET solution, which is why this went unnoticed.

**Important:** do not fix this by re-adding `DisposeAllSegments`. `git log -S`
confirms `42905cd` removed it deliberately when `ScopedBufferPool` took over
teardown. The tests encode a superseded design and need rewriting against the
current one. See issue 6 in [known-issues-hls.md](known-issues-hls.md).

These tests cover the buffer-lifetime code, which is the subtlest part of the
codebase and the subject of most of the HLS findings. Restoring them is a
prerequisite for fixing those safely.

**Fix:** update the three test files to the current API, then add a CI workflow
that runs `dotnet build` and `dotnet test` on push and PR.

**Sequencing:** do
[enhancement-core-extraction.md](enhancement-core-extraction.md) *first*. It
changes the very API these tests target — `GetSegment`/`GetPlaylist` stop
returning `IResult`, and the ingester stops taking `HttpRequest` — so rewriting
them against today's shape means rewriting them twice. It also makes the state
machine testable without a web host, which is what these tests actually need.

---

## 2. Vulnerable transitive packages

**Severity:** Medium · **Area:** `TVRoom/TVRoom.csproj`

`dotnet list package --vulnerable --include-transitive` reports eight High
advisories across both projects:

```
SQLitePCLRaw.lib.e_sqlite3        2.1.10   High   GHSA-2m69-gcr7-jv3q
System.Security.Cryptography.Xml  9.0.0    High   GHSA-37gx-xxp4-5rgx
                                                  GHSA-w3x6-4m5h-cxqf
                                                  GHSA-cvvh-rhrc-wg4q
                                                  GHSA-g8r8-53c2-pm3f
                                                  GHSA-23rf-6693-g89p
                                                  GHSA-8q5v-6pqq-x66h
                                                  GHSA-mmjf-rqrv-855v
                                                  GHSA-6588-8gv4-xfgh
```

Every direct dependency is pinned at `9.0.0` from November 2024.
`System.Security.Cryptography.Xml` arrives via
`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` and sits on the live
path for data-protection key handling, so it is not a dormant reference.

**Fix:** bump to the latest 9.0.x patches. Worth doing together with issue 1 so
there is a test run to validate against. `Vite.AspNetCore` was already flagged as
needing attention in `6379ada` and is still on 1.12.0.

---

## 3. Forwarded headers are accepted from any client

**Severity:** Medium-low · **Area:** [Program.cs:27-35](../TVRoom/Program.cs#L27-L35)

```csharp
options.KnownProxies.Clear();
options.KnownNetworks.Clear();
```

Clearing both disables the check on who is allowed to set `X-Forwarded-*`, so any
client can supply `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host`.
`AllowedHosts` is `"*"` in `appsettings.json`, so nothing downstream constrains
the host either.

The concrete consequence: `/broadcast/current` builds the stream URL it hands
back from `request.Scheme` and `request.Host`
([BroadcastApiEndpoints.cs:57](../TVRoom/Broadcast/BroadcastApiEndpoints.cs#L57)),
so a spoofed `Host` header changes the URL clients are told to fetch. A spoofed
`X-Forwarded-For` also means request logs, and any future rate limiting, can be
poisoned.

This is the documented-insecure configuration, and it is a common shortcut for
containers behind a reverse proxy whose IP is not known ahead of time. It is only
defensible while the app is unreachable except through that proxy.

**Fix:** set `KnownProxies` to the reverse proxy address (or `KnownNetworks` to
its subnet), and set `AllowedHosts` to the real hostname.

---

## 4. HLS ingest endpoints are anonymous and bound to every interface

**Severity:** Low-medium · **Area:** [TranscodeApiEndpoints.cs:44](../TVRoom/Transcode/TranscodeApiEndpoints.cs#L44)

`group.AllowAnonymous()` covers the three `PUT /transcode/{transcodeId}/...`
routes that ffmpeg writes into. `HlsConfiguration` builds `HlsIngestBaseAddress`
as `http://127.0.0.1:{port}/transcode`, so the intent is clearly loopback-only,
but nothing enforces it — the endpoints listen wherever Kestrel listens.

What keeps this from being serious: `transcodeId` is 32 characters drawn from a
36-character alphabet via `RandomNumberGenerator`
([TranscodeSession.cs:76](../TVRoom/Transcode/TranscodeSession.cs#L76)), so it is
an unguessable capability URL, and it appears only in
`BroadcastInfo.FFmpegArguments`, which is exposed solely through the admin-gated
`ControlPanelHub`. An attacker who obtained one could inject arbitrary segments
and playlists into a live broadcast.

Note also that the group convention applies to endpoints added *after* the
`AllowAnonymous()` call, because group conventions are applied at build time — so
`/transcode/bufferstats` is anonymous too. That one exposes only `SharedBuffer`'s
static rented-buffer counters, but it is likely unintended.

**Fix:** restrict the ingest routes to loopback, either by binding them to a
separate local-only listener or with a short middleware check on
`Connection.RemoteIpAddress`. Move the `bufferstats` map above the
`AllowAnonymous()` call, or give it an explicit policy.

---

## 5. Smaller items

**`IsInvalidFileName` is dead code.**
[BroadcastApiEndpoints.cs:66](../TVRoom/Broadcast/BroadcastApiEndpoints.cs#L66)
is unreferenced. The identical copy in
[BroadcastLogEndpoints.cs](../TVRoom/Broadcast/BroadcastLogEndpoints.cs) is the
live one, and it does correctly block traversal on both Windows and Linux — on
Linux `Path.GetInvalidFileNameChars()` returns only `\0` and `/`, but rejecting
`/` is sufficient, because `Path.Combine` will not traverse without it.

**Race in `BroadcastManager.StartSession`.** The `_currentSession is not null`
guard and the later assignment are not atomic, so two concurrent start calls can
both get past the check. Admin-only and unlikely in practice; the fix is a lock
or an `Interlocked.CompareExchange`.

**Only `InvalidJwtException` is caught in `/signin`.** A transport failure while
fetching Google certificates surfaces as an unhandled 500 rather than a clean
error.

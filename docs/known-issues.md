# Known issues — repo-wide

Open items from the code review of 2026-09-01, minus the two that were fixed
(see [README.md](README.md)). Ordered by how much they matter.

---

## 1. Nothing builds or tests the solution

**Severity:** Medium (process) · **Area:** `.github/workflows/`

The tests themselves were fixed in `be1ce5f` and now pass — 33 tests, green. What
remains is that **no automated job runs them.** The only workflow in
`.github/workflows/` deploys the cast receiver to GitHub Pages; nothing runs
`dotnet build` or `dotnet test`.

That gap is why the test project sat broken from November 2024 until September
2026 without anyone noticing. Tests that nobody runs decay back to the same state.

**Fix:** add a workflow running `dotnet build` and `dotnet test` on push and PR.

Consider also treating warnings as errors in CI. The build is currently clean —
0 warnings, 0 errors — so a `-warnaserror` gate would hold today and would catch
regressions like the `ASPDEPR005` deprecation that the .NET 10 upgrade surfaced.

---

## 2. Forwarded headers are accepted from any client

**Severity:** Medium-low · **Area:** [Program.cs:27-35](../TVRoom/Program.cs#L27-L35)

```csharp
options.KnownProxies.Clear();
options.KnownIPNetworks.Clear();
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

**Fix:** set `KnownProxies` to the reverse proxy address (or `KnownIPNetworks`
to its subnet), and set `AllowedHosts` to the real hostname.

---

## 3. HLS ingest endpoints are anonymous and bound to every interface

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

## 4. Smaller items

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

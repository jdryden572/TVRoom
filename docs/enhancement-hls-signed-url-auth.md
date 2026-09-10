# Enhancement — signed URL tokens for HLS stream endpoints

**Status:** Proposed, not started · **Raised:** 2026-09-04

Add real authentication to the `/streams/*` endpoints
([BroadcastApiEndpoints.cs](../TVRoom/Broadcast/BroadcastApiEndpoints.cs)), which
are currently `AllowAnonymous()`.

---

## Goal

Bind stream access to an authenticated user, with an expiry and the ability to
revoke, **without requiring changes to any player**.

## The constraint that decides the design

Playback happens across five paths with very different capabilities:

| Client | Custom headers | Cookies | Notes |
| --- | --- | --- | --- |
| Web desktop (video.js 8.10 → VHS) | Yes, `videojs.Vhs.xhr.beforeRequest` | Yes, same-origin | |
| Web iOS/Safari (native HLS) | **No** | Yes, same-origin | video.js hands off to the OS |
| Chromecast (CAF + shaka) | Yes, `manifestRequestHandler` / `segmentRequestHandler` | **No** | Receiver is served from GitHub Pages — separate device *and* origin |
| AirPlay (silvermine plugin) | **No** | **No** | The Apple TV fetches the URL itself |
| Android (ExoPlayer/Media3) | Yes, `setDefaultRequestProperties` | Awkward | App is not in this repo; capability assumed from the library |

AirPlay and native-HLS Safari are the binding constraint. Neither lets you attach
anything to the request, so **the only credential that works everywhere is one
carried in the URL itself.**

## What exists today

`sessionId` is 32 characters over a 36-character alphabet from
`RandomNumberGenerator` (~165 bits) — see `GenerateSessionId` in
[BroadcastSessionFactory.cs](../TVRoom/Broadcast/BroadcastSessionFactory.cs). Every
place that hands it out is already authenticated:

- [Index.cshtml:13](../TVRoom/Pages/Index.cshtml#L13) — server-rendered, behind the Viewer fallback policy
- [ControlPanel.svelte:54](../TVRoom/client/src/lib/ControlPanel.svelte#L54) — admin-gated page
- [BroadcastApiEndpoints.cs:57](../TVRoom/Broadcast/BroadcastApiEndpoints.cs#L57) — `/broadcast/current`, requires `RequireApiViewer`

So this is already an unguessable capability URL, not an open endpoint. The gaps
worth closing: it is valid for the whole broadcast, identical for every viewer,
cannot be revoked, and leaks through proxy logs, browser history, and Cast LOAD
messages.

That every hand-off point is *already* authenticated is what makes this
enhancement cheap — the token can be minted at those points with no client work.

---

## Design: path-embedded signed token

Change the route shape to put the token in the **path**:

```
/streams/{token}/{sessionId}/master.m3u8
/streams/{token}/{sessionId}/live.m3u8
/streams/{token}/{sessionId}/live{index}.ts
```

### Why the path and not a query string

This is the load-bearing detail; do not "simplify" it to `?t=...` later.

HLS playlists here use relative references — `MasterPlaylistResult.WriteTo`
emits `live.m3u8` and `HlsSegmentEntry.WriteTo` emits `live{Index}.ts`, both in
[HlsStreamState.cs](../TVRoom/HLS/HlsStreamState.cs). Relative resolution
**drops the query string**: `live0.ts` against `.../live.m3u8?t=X` resolves to
`.../live0.ts` with no token, so a query-string design forces you to append the
token to every URI inside those two `Utf8.TryWrite` hot paths and thread the
token down into `HlsSegmentEntry`.

A path prefix is inherited by relative references automatically, so **the
playlist writers do not change at all**.

### Signing

Use ASP.NET Core Data Protection, which handles key management and rotation:

```csharp
var protector = provider.CreateProtector("hls-stream").ToTimeLimitedDataProtector();
var token = protector.Protect(sessionId, DateTimeOffset.UtcNow.AddHours(5));
// validate: Unprotect(token) == sessionId, else Results.NotFound()
```

Return `404`, not `401` — players handle it better and it does not confirm
whether a session exists.

### Prerequisite: Data Protection keys are not actually persisted

`TVRoomContext` implements `IDataProtectionKeyContext` and exposes a
`DataProtectionKeys` DbSet, and the `20240713195933_InitialCreate` migration
creates the table — but **`AddDataProtection()` is never called anywhere in the
source**, so `PersistKeysToDbContext<TVRoomContext>()` is not wired up and that
table is unused. Keys fall back to the default provider location, which is
ephemeral in the container.

This must be fixed first, or every container restart invalidates all outstanding
tokens. It is very likely already causing auth cookies to be invalidated on each
deploy, so it is worth fixing regardless of this enhancement.

```csharp
builder.Services.AddDataProtection().PersistKeysToDbContext<TVRoomContext>();
```

---

## Implementation steps

1. Wire up `AddDataProtection().PersistKeysToDbContext<TVRoomContext>()` in
   [Program.cs](../TVRoom/Program.cs). Verify keys land in the `DataProtectionKeys`
   table and survive a restart.
2. Add a small token service (mint + validate) wrapping the time-limited
   protector, registered in DI.
3. Change the three `/streams` routes to include `{token}` ahead of
   `{sessionId}`, and validate in an endpoint filter on the group. Keep
   `RequireCors("AllowAll")` and `AllowAnonymous()` on the group — the token *is*
   the credential, and keeping requests as simple GETs avoids a CORS preflight
   per segment.
4. Mint at the three hand-off points listed above. Mint **per request**, not once
   at broadcast start, so the lifetime begins when the viewer asks rather than
   when the broadcast started.
5. Leave the playlist writers alone. Confirm with a real player that relative
   resolution carries the prefix.

### Token lifetime

Broadcasts run up to `MaxDuration` (4h by default), and a token that expires
mid-stream breaks playback. Start with a lifetime slightly longer than
`MaxDuration`, scoped to the session. That is no worse than today's exposure
while adding expiry, revocability, and user binding.

If tighter windows are wanted later: give the *playlist* a session-length token
and mint short-lived per-segment tokens on each playlist response. That requires
the query-string variant and the writer changes described above, so only do it if
the threat model actually demands it.

---

## Rejected alternatives

Recorded so they are not relitigated.

- **Bearer token via player request hooks.** Server side already exists
  (`AddBearerToken()` and `/signin`). Works for desktop web, Cast, and Android;
  **breaks AirPlay and iOS Safari**. Also makes every request non-simple, adding a
  CORS preflight per segment — at 2-second segments that doubles request count,
  and `Access-Control-Max-Age` is capped at 2h in Chrome and lower in Safari.
  Viable only as a second factor on clients that support it.
- **Cookie auth.** Fine for same-origin web including iOS native HLS; useless for
  Cast and AirPlay. Also incompatible with the current CORS policy —
  `AllowCredentials` cannot combine with `AllowAnyOrigin`, and the Cast receiver
  is cross-origin.
- **HLS AES-128 with an authenticated key endpoint.** The `#EXT-X-KEY` request has
  the same credential-passing problem, plus ffmpeg `-hls_key_info_file` plumbing.
  Not worth the machinery here.
- **Network scoping (RFC1918 only).** Cheap and strong if viewing is LAN-only, but
  does not cover remote access. Composes well with this design rather than
  replacing it.

## Risks and watch items

- Tokens in URLs land in access logs, browser history, and Cast LOAD messages.
  Acceptable for a home server, but it is why the lifetime should be bounded.
- If tokens are bound per user, make sure the Cast **sender** passes its own token
  in the LOAD message rather than the receiver minting one — the receiver has no
  credentials of its own.
- The `/broadcast/current` response shape changes (the URL gains a path segment).
  Confirm the Android app builds nothing by hand from `sessionId`.
- Worth checking whether AirPlay is actually in use before weighting it heavily;
  it is the main reason header-based auth is off the table.

## Decided

- **Bind the token to the user, and include a nonce generated per URL hand-off.**
  Per-user gives revocation and costs only a distinct URL per viewer. The nonce
  distinguishes one user's phone from their TV, which is required by
  [enhancement-viewer-presence.md](enhancement-viewer-presence.md) — without it,
  two devices are indistinguishable from one session. Decided 2026-09-05 when
  that feature was specified; it is cheap to include now and expensive to
  retrofit, since the token payload shape is what changes.

## Open questions

- Should an admin "stop broadcast" invalidate outstanding tokens immediately, or
  is expiry enough? A nonce in the payload makes per-viewer revocation cheap if
  it is wanted.

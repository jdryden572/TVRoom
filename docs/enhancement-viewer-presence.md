# Enhancement — see who is watching a broadcast

**Status:** Proposed, not started · **Raised:** 2026-09-05

Show which authorized users are currently viewing a broadcast, and optionally
keep a history of who watched what.

**Depends on** [enhancement-hls-signed-url-auth.md](enhancement-hls-signed-url-auth.md).
That task supplies the identity this feature reads; there is no way to build this
without it (see *Why this needs signed URLs* below).

---

## Why this needs signed URLs

`/streams/*` requests currently carry no identity whatsoever. Every alternative
way of adding it fails on the same clients that ruled out header-based auth in
the auth task:

- **Cookies** — dead on Chromecast and AirPlay, which are separate devices.
- **SignalR presence from the player** — covers the web player only, not Cast,
  AirPlay, or Android.
- **Access log parsing** — gives IP addresses, not users.

Once the token is in the URL and bound to a user, every stream request
self-identifies, uniformly across all clients, with no per-client work.

## The heartbeat already exists

No new signal is needed. With `HlsTime = 2`, every active player refetches
`live.m3u8` roughly every two seconds for as long as it is watching — that is how
live HLS works. So:

> A viewer is active if a playlist request bearing their token arrived within the
> last N seconds.

Set N to roughly 15s — several missed polls — to tolerate hiccups.

### Track on the playlist request only

Two reasons, both important:

1. **Segment requests are the hot path.** `GetSegment`
   ([HlsStreamState.cs:97](../TVRoom/HLS/HlsStreamState.cs#L97)) does a lock-free
   state read and rents a buffer. Do not add a dictionary write there.
2. **The playlist is the better signal.** A stalled or rebuffering player keeps
   polling the playlist even when it is not pulling segments, which is exactly the
   "still connected" semantic wanted here.

---

## Design

### Identity granularity: per user, with a nonce

Mint the token with both the user and a nonce, where the nonce is generated per
URL hand-off rather than per user. That gives two useful rollups:

- Group by user — "who is watching"
- Group by nonce — "James is watching on 2 devices"

Without the nonce, one user's phone and TV are indistinguishable from a single
session. This decision is recorded in the auth task's open questions.

### Storage: in-memory for live, optional table for history

For the live view, a `ConcurrentDictionary<nonce, ViewerHeartbeat>` holding
`(user, firstSeen, lastSeen)`, swept periodically or lazily on read. One
`AddOrUpdate` per viewer per two seconds is negligible. Persisting every poll
would be absurd I/O and is not proposed.

If watch history is wanted, open a row on first sighting and close it when the
heartbeat lapses — the same shape as `BroadcastSessionRecord`
([BroadcastSessionRecord.cs](../TVRoom/Persistence/BroadcastSessionRecord.cs)).
Note it would need the same startup sweep for rows orphaned by a crash that
issue is already flagged for broadcast history in
[enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md).

Treat history as a second, separable phase. The live view is the feature; history
is a nice-to-have with real storage-growth and retention questions attached.

### Lifetime and ownership

The registry is per-broadcast. It should be owned by `BroadcastSession` and torn
down with it, so a new broadcast starts with an empty viewer list and a stopped
broadcast does not leak entries.

If [enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md) lands
first, the mailbox loop is the natural owner of that teardown.

### Surfacing: administrators only

**Decided 2026-09-05: viewer stats are visible to administrators only, not to
other viewers.** Who else is home and watching is not something the audience
should learn from the app.

The straightforward implementation already satisfies this: put the stream on
`ControlPanelHub`, which is `[Authorize(Policies.RequireAdministrator)]`
([ControlPanelHub.cs](../TVRoom/Broadcast/ControlPanelHub.cs)), sampled about once
a second like `GetTranscodeStats`
([ControlPanelHub.cs:84](../TVRoom/Broadcast/ControlPanelHub.cs#L84)).

Expose it as `IAsyncEnumerable<T>` per
[enhancement-streaming-seam-types.md](enhancement-streaming-seam-types.md), with
an explicit drop policy — for a viewer list, dropping intermediate updates is
fine, since only the latest matters.

#### Consequence for the `BroadcastStatus` object

An earlier draft of this section suggested folding viewer data into the single
`BroadcastStatus` record proposed in
[enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md). **Do not
do that without splitting the projection.** That task collapses broadcast state
into one object served three ways, and one of those ways is
`/broadcast/current`, which requires only `RequireApiViewer`
([BroadcastApiEndpoints.cs:49](../TVRoom/Broadcast/BroadcastApiEndpoints.cs#L49)) —
so a viewer field on the shared object would leak the viewer list to every
authorized viewer through the API.

If the two tasks are combined, `BroadcastStatus` needs an admin-only projection
distinct from the viewer-facing one, and the viewer list belongs only in the
former. Keeping viewer presence on its own admin-gated stream avoids the problem
entirely and is the safer default.

---

## Implementation steps

1. Land the signed-URL auth task with per-user-plus-nonce tokens.
2. Add the viewer registry, owned by `BroadcastSession`.
3. Record a heartbeat in the `live.m3u8` handler in
   [BroadcastApiEndpoints.cs](../TVRoom/Broadcast/BroadcastApiEndpoints.cs), after
   token validation and before serving the playlist. Do not touch the segment
   handler.
4. Sweep entries older than the timeout; expose the current list.
5. Surface it on the control panel.
6. *(Optional, later.)* Persist view sessions for history.

---

## Limitations to state up front

These are inherent, not implementation shortcuts. Worth putting in the UI copy so
the numbers are not over-trusted.

- **There is no disconnect event.** HLS is stateless polling, so departure is
  always inferred from a lapsed heartbeat. "Stopped watching" lags by the timeout
  window. This cannot be fixed; it is the protocol.
- **Cast and AirPlay attribute to the sender.** The receiver device fetches the
  stream, so the request IP and user-agent belong to the TV while the identity is
  whoever minted the token in the sender app. That is arguably the correct
  attribution, but network-level details will not match the person.
- **A shared URL is attributed to the original user.** Inherent to any
  bearer-style credential. The nonce at least makes one token in use from two
  places at once visible, which is the signal worth alerting on.
- **Idle tabs count as viewing.** A player left open in a background tab keeps
  polling. If "actively watching" is wanted rather than "connected", segment
  fetches are the stricter signal — a later refinement, not the initial cut, given
  the hot-path concern above.

## Open questions

- Retention, if history is built. Broadcast history is currently unbounded too.
- Should an admin be able to revoke a specific viewer's token, or is that
  overkill? It becomes cheap once tokens carry a nonce.

# Architecture — server/agent split

**Status:** Design agreed, not started · **Raised:** 2026-09-05

Split TVRoom into a centralized server and one or more agents deployed near a
tuner. This is a large change that will need decomposing into tasks before any
work starts — see [Decomposition sketch](#decomposition-sketch) at the end.

---

## Motivation

Today the server runs on the residential network with the tuner, and streams HLS
directly to remote viewers. **Every remote viewer costs another copy of the
stream on a residential upload link.** Three people watching means three times the
outbound bitrate from a connection that has little headroom to give.

With an agent, exactly one copy leaves the residential network regardless of
audience size. The server fans out from wherever it is hosted, where bandwidth is
cheap.

Secondary goal: keep a path open to lower latency later. It is a nice-to-have,
not a requirement, but it should not be designed out.

## Shape

| | Runs on agent (residential) | Runs on server (central) |
| --- | --- | --- |
| Tuner access | yes | no |
| ffmpeg h264 encode (hardware accelerated) | yes | no |
| Segmentation into HLS | no | yes |
| Playlist generation, client serving | no | yes |
| Broadcast lifecycle, users, history, config | no | yes |
| Admin UI | no | yes |

The agent is deliberately thin: it holds no configuration beyond how to reach the
server, and it makes no decisions. Transcode parameters continue to live in the
server's database and admin UI, and are delivered per broadcast.

---

## Where `IAgentProxy` fits

`IAgentProxy` sits between the broadcast state machine and whatever actually runs
ffmpeg. The essential property: **video never flows through it.** It carries
control and telemetry only.

```
ControlPanelHub                              <- admin commands
      |
BroadcastManager ---- mailbox + state machine + transition log
      |  commands                    ^  events
      v                              |  (TranscodeEnded, StreamReady)
  IAgentProxy -----------------------+        transport only, no decisions
      |
      +-- LocalAgent  -> ffmpeg here ----------------+
      +-- RemoteAgent -> SignalR -> agent ffmpeg     |
                                   -> SRT -> remux --+
                                                     v
BroadcastSession (server) <- HlsFileIngester <- /transcode ingest
      +-- MergedHlsLiveStream -> HlsStreamState -> /streams/* -> viewers
```

Both implementations converge on the same ingest endpoints, which is exactly why
Decision 1 chose server-side remux: the HLS assembly half never learns which one
is in use.

This also explains the split-brain problem below. Control and media are
**separate paths**, so a dropped control channel does not stop video arriving —
which is why `OnDisconnectedAsync` must not tear down a broadcast.

| Owns | Does not own |
| --- | --- |
| Sending commands | Broadcast state — that is the manager's state machine |
| Surfacing events and telemetry | HLS assembly, playlist generation, viewer serving |
| Enough state to answer "is a transcode running?" for reconnect reconciliation | Any decision at all |

The seam's real payoff is that **single-box stays a supported deployment**.
`LocalAgent` is not a test double; it is the configuration running today, and it
keeps working unchanged.

### Changes the current interface needs

The sketch on `refactor-for-agents` predates the decisions below and needs three
corrections:

- **Drop `WaitForTranscodeToEndAsync()`.** It is a blocking call in what has
  become a message-passing design. Nothing awaits the transcode ending — the
  agent posts `TranscodeEnded` and the loop handles it. See
  [enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md).
- **`StartTranscodeCommand` carries the wrong thing.** It takes a `ChannelInfo`,
  whose `Url` is the LAN-only tuner URL that Decision 6 says must never leave the
  agent. It should carry `GuideNumber` and let the agent resolve locally. It also
  needs an **output target** — the loopback ingest URL for `LocalAgent`, the SRT
  URL and passphrase for `RemoteAgent` — which is the same concept
  `HlsIngestBaseAddress` already represents.
- **Add `GetChannelsAsync`.** Decision 6 gives the agent tuner ownership, so
  channel listing belongs here alongside `GetTunerStatuses`.

A fourth is tracked separately: `LocalAgent.GetDebugOutput` drops its backpressure
policy and ignores its cancellation token, in
[enhancement-streaming-seam-types.md](enhancement-streaming-seam-types.md).

### Why `LocalAgent` looks right but is not

`LocalAgent` holds a `BroadcastSession` as its `_currentSession`. That conflates
two things which are only the same object in single-box mode:

- **A transcode** — an ffmpeg process producing segments
- **A broadcast** — a viewer-facing stream with a playlist, a readiness state, and
  continuity across transcode restarts

Split them and they land on opposite sides of the boundary: the agent runs an
encoder, the server assembles the broadcast. The agent should hold *transcode*
state only, which is also what makes reconnect reconciliation tractable — an
agent reporting "transcoding session X" is a far smaller claim than one reporting
broadcast state.

---

## Decision 1 — contribution format

**Agent → SRT (MPEG-TS) → server-side ffmpeg `-c copy -f hls` → existing ingest
endpoints.**

### Why not "agent forms HLS and ships segments"

That is closest to today's design — ffmpeg already PUTs HLS segments over HTTP to
the ingest endpoints — but it degrades badly over a residential WAN link:

- **Failure handling does not exist today**, because loopback never fails.
  ffmpeg's HTTP muxer errors out on a failed PUT and the transcode dies. The agent
  would need to spool segments and retry, which is real work the "it already
  works" argument tends to hide.
- **The ingest correlation assumes near-ordered arrival.**
  `ProcessIngestedFiles` keeps **at most one** segment queued
  ([HlsFileIngester.cs:70](../TVRoom/HLS/HlsFileIngester.cs#L70)); a second segment
  arriving before the first one's playlist silently drops the first. Over loopback
  with ordered writes this never fires. Over a WAN with retries or reordering it
  drops video.
- **It pins the delivery format at the far end of the link.** Agents live in
  someone else's house and are the hardest component to redeploy. Format decisions
  belong on the server.

### Why SRT

Residential uplinks are unmanaged — variable bandwidth, bufferbloat, transient
loss. SRT is built for exactly this: a configurable latency window (typically 3–4×
RTT) with ARQ retransmission inside it, so jitter and modest loss are absorbed
rather than becoming visible gaps. It also encrypts the payload, which matters
because this crosses the public internet.

NAT settles the direction: the agent cannot accept inbound connections, so
**agent is the SRT caller, server is the listener.**

### Why server-side ffmpeg rather than an in-process segmenter

The remux keeps the entire existing HLS pipeline unchanged. The server-side ffmpeg
produces exactly the output the ingest already consumes, including the
`#EXT-X-STREAM-INF` and version metadata that `HlsSegmentInfo` carries. The remux
is `-c copy` — no decode or encode, so it is cheap.

It also reuses machinery that already exists: `FFmpegProcess`
([FFmpegProcess.cs](../TVRoom/Transcode/FFmpegProcess.cs)) already handles spawn,
stderr capture, graceful `q` shutdown, and kill-on-timeout.

An in-process segmenter remains the later move if per-broadcast ffmpeg processes
on a shared server become a problem. fMP4/CMAF would be easier to segment
in-process than TS, since fragment boundaries are self-delimiting and align to
keyframes.

### On latency

Continuous contribution does **not** reduce latency by itself. The floor is set by
the player-facing format:

| Stage | Cost |
| --- | --- |
| Hardware encode | negligible |
| Segment must close before it can be referenced | `hls_time` = 2s |
| Player buffers ~3 segments before starting | ~6s |
| **Glass-to-glass** | **~8s** |

You pay the segment duration whichever side of the wire cuts it. What *does*
reduce latency is LL-HLS — ~250–500ms partial segments with blocking playlist
reloads — which requires a continuous contribution stream to be possible at all.

So this decision is not a latency win; it is **the prerequisite for a future
latency win** — one that requires **no agent redeploy**, which is the point. It is
not, however, free on the client side; see below.

### LL-HLS readiness, if it is ever wanted

LL-HLS is **additive**. A player that does not understand `#EXT-X-PART` ignores
those tags and plays the full segments at today's latency, so it can ship
server-side without breaking any existing client. Nothing below is a forced
migration — it is per-player tuning to actually realise the benefit.

| Client | Support | Work |
| --- | --- | --- |
| AirPlay (tvOS 14+) | Native | None — Apple designed LL-HLS |
| Safari / iOS web (iOS 14+) | Native | None |
| Android (ExoPlayer/Media3 2.15+) | Yes | Set a target live offset |
| Chromecast (CAF + shaka) | Library supports it; **bundled version is the question** | Verify what the CAF SDK ships, on real hardware |
| Web non-Safari (video.js 8.10 + VHS) | Weakest of the five | Likely a version bump and a config flag |

The two uncertain rows need testing against real devices, not documentation:

- **video.js / VHS** support began as an `experimentalLLHLS` opt-in and has been
  the least mature major implementation, particularly around blocking playlist
  reloads and preload hints. Prototype this one first — it is both the primary web
  path and the most likely to disappoint. If it does, the escape hatch is swapping
  the web player to shaka or hls.js; a bigger change, but contained to one page.
- **Chromecast** uses `useShakaForHls: true`, so the shaka build is CAF's, not one
  you control, and it likely varies by device generation. If it disappoints, a
  custom receiver can load its own shaka build rather than delegating.

**Infrastructure, not player code.** LL-HLS changes the request pattern
substantially — a continuously held blocking playlist request plus a fetch per
part, roughly 4–10× the request rate. That means HTTP/2 to clients becomes close
to mandatory; blocking reloads shift Kestrel's profile from short requests to many
long-lived ones; and the reverse proxy must not buffer playlist responses or
blocking reloads break (`proxy_buffering off` on the playlist route in nginx).

**One decision already pays off here.** The path-embedded token from
[enhancement-hls-signed-url-auth.md](enhancement-hls-signed-url-auth.md) handles
partial segments for free — parts are referenced by relative URI just like full
segments, so they inherit the `/streams/{token}/{sessionId}/` prefix. A
query-string token would have needed appending to every `#EXT-X-PART` URI,
multiplying the exact problem that made path-based the right call, since parts
outnumber segments several times over.

---

## Decision 2 — the restart-without-disconnect feature is preserved

The ability to restart a hung or failed ffmpeg without disconnecting viewers is a
required feature. It survives this change untouched, because it is **not**
format-specific:

| Concern | Where it lives | Format-specific? |
| --- | --- | --- |
| Correlate playlist + segment PUTs → `HlsSegmentInfo` | `HlsFileIngester`, `ParsedMasterPlaylist`, `ParsedStreamPlaylist` | **Yes** |
| Switch between successive transcodes | `MergedHlsLiveStream._sources.Switch()`, `SetNewSource()` | No |
| Continuous segment numbering across restarts | `HlsStreamState.WithNewSegment` (`latestIndex + 1`) | No |
| `#EXT-X-DISCONTINUITY` at the join | `WithNewDiscontinuity()` | No |

`MergedHlsLiveStream` takes `IObservable<HlsSegmentInfo>` in its constructor
([MergedHlsLiveStream.cs:16](../TVRoom/HLS/MergedHlsLiveStream.cs#L16)) and in
`SetNewSource` ([:34](../TVRoom/HLS/MergedHlsLiveStream.cs#L34)). **That interface
is the seam.** Anything producing `HlsSegmentInfo` plugs into the merge machinery.

Under the chosen design nothing changes at all: the server-side remux produces
HLS, the existing ingester consumes it, and a restart follows the identical path —
new `TranscodeSession` → new ingester → `SetNewSource()` → discontinuity,
continuous numbering, no client disconnect.

**Open detail:** with server-side segmentation, a stream that dies mid-segment
leaves a partial. Agent-side HLS only ever shipped complete segments — that
atomicity is genuinely given up here. Decide whether to close the partial early
(HLS tolerates variable durations under target) or discard it.

---

## Decision 3 — media path authentication

**Per-broadcast SRT passphrase, generated on the server, delivered over the
control channel.**

SRT offers two primitives. The **passphrase** (10–79 chars, `pbkeylen=32` for
AES-256) is a pre-shared secret used in the keying-material exchange; it never
crosses the wire, and a caller with the wrong one fails the handshake — so it
provides encryption and authentication-by-shared-secret together. **StreamID** is
the conventional place for an identity token, but **ffmpeg's listener has no hook
to inspect it and reject a connection**, so it cannot be used for auth while
ffmpeg is the receiver.

That leaves one listener, one passphrase — which fits, because there is already
one remux process per broadcast:

1. Admin starts a broadcast on the server.
2. Server allocates a port, generates a random passphrase, starts the remux ffmpeg
   as SRT listener.
3. Server instructs the agent over the control channel: push to
   `srt://host:port?passphrase=…`.
4. Agent starts its encoder as SRT caller.

The control channel does the real authentication; the passphrase is a short-lived
single-use capability for the media path. This is the same pattern as the signed
stream URLs in
[enhancement-hls-signed-url-auth.md](enhancement-hls-signed-url-auth.md):
authenticate on a channel you control, then issue an ephemeral per-session
credential for the media path.

**A VPN was considered and rejected.** WireGuard or Tailscale would give stronger
auth and remove the public listener, but adds per-site enrolment to agent
deployment. Keeping agent provisioning to "drop in a token" was judged more
valuable.

### Consequences

- **The passphrase must be redacted in three places.** ffmpeg only accepts it as a
  URL parameter, so it lands in the argument string — which is logged at
  Information level ([FFmpegProcess.cs:48](../TVRoom/Transcode/FFmpegProcess.cs#L48)),
  carried to the control panel as `BroadcastInfo.FFmpegArguments`
  ([BroadcastInfo.cs:5](../TVRoom/Broadcast/BroadcastInfo.cs#L5)), and written to
  the per-broadcast log file by `WriteTranscodeLogsToFile`, which is downloadable
  through the admin `/logs` endpoints. Easiest fix is to keep the SRT URL out of
  the exposed argument string entirely rather than pattern-matching it out. It is
  also visible in `ps` on the server.
- **`listen_timeout`** on the listener, so a no-show agent fails fast rather than
  hanging a process.
- **Ephemeral port is a security benefit** — the listener only exists while the
  remux process runs, so there is no permanently open contribution port.
- **A UDP port range** must be opened, sized to concurrent broadcasts.
- **libsrt becomes internet-facing.** A C library parsing untrusted handshakes on
  a public socket, live whenever a broadcast is running. Track its version the way
  you would any internet-facing dependency. NuGet packages are current as of the
  .NET 10 upgrade, but the base image is still pinned at `ffmpeg:7.0.2` — see
  *Already addressed* in [README.md](README.md) for why that pin was left alone.

---

## Decision 4 — control channel: SignalR

NAT inverts the natural direction. Commands flow server→agent, but the agent must
dial. A long-lived duplex connection gives server-initiated semantics over an
agent-initiated connection, and doubles as liveness: connected means present.

**SignalR was chosen over gRPC duplex streaming.** Both work; SignalR wins on
practical grounds:

| | SignalR | gRPC duplex |
| --- | --- | --- |
| Already in codebase | yes | new dependency |
| Reconnection | `WithAutomaticReconnect()` | you write it |
| Request/response to agent | client results (.NET 7+) | hand-rolled `request_id` envelope |
| Proxy compatibility | WebSocket, trivial | needs HTTP/2 end-to-end |
| Per-stream flow control | **no** | yes |
| Versioned contract | shared assembly, by convention | `.proto`, by construction |

Client results are the decisive convenience: a typed-client method returning
`Task<T>` makes the server `await` a real return value from the agent, which is
the correlation gRPC duplex would require plumbing by hand.

### Shape

Follows the existing `Hub<TClient>` pattern from
[ControlPanelHub.cs](../TVRoom/Broadcast/ControlPanelHub.cs):

```csharp
public interface IAgentClient
{
    Task<StartTranscodeResult> StartTranscode(StartTranscodeCommand command);
    Task StopTranscode();
    Task RestartTranscode();
}

[Authorize(Policies.RequireAgent)]
public sealed class AgentHub : Hub<IAgentClient>
{
    public override Task OnDisconnectedAsync(Exception? ex)
        => _agents.MarkDisconnected(Context.ConnectionId);   // does NOT stop the broadcast

    public Task ReportState(AgentState state) => _agents.Reconcile(Context.ConnectionId, state);
    public Task TranscodeEnded(string sessionId) => _broadcasts.Post(new TranscodeEnded(sessionId));

    public async Task PushDebugOutput(IAsyncEnumerable<string> lines) { await foreach (var l in lines) … }
    public async Task PushStats(IAsyncEnumerable<TranscodeStats> stats) { … }
}
```

Agent side:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl($"{serverUrl}/agentHub", o => o.AccessTokenProvider = () => Task.FromResult(agentToken))
    .WithAutomaticReconnect()
    .Build();

connection.On<StartTranscodeCommand, StartTranscodeResult>("StartTranscode", StartAsync);
connection.Reconnected += async _ => await connection.InvokeAsync("ReportState", CurrentState);
```

### Accepted costs

- **No per-stream flow control.** Everything multiplexes over one WebSocket, so a
  burst of ffmpeg stderr can delay a command. Mitigated by demand-driven telemetry
  (below); if isolation is still wanted, open a second `HubConnection` to a
  separate telemetry hub.
- **No IDL.** Mitigate with a shared contracts assembly, versioned deliberately,
  and additive-only discipline — SignalR's JSON protocol ignores unknown
  properties and defaults missing ones, so adding optional fields is safe while
  renaming or removing is not.

### Make telemetry demand-driven

Worth doing regardless of transport. Today `BroadcastSession` subscribes to ffmpeg
stderr unconditionally and keeps a 60-second replay buffer whether or not an admin
is watching. On loopback that is free; over a residential uplink it spends the
exact resource this architecture exists to conserve, pushing debug output to
nobody.

Have the server send `StartDebugStream` / `StopDebugStream` when an admin opens
and closes the control panel. In the common case there is no telemetry flood at
all, which also removes the scenario where stderr competes with a `Stop` command.

**This applies to ffmpeg stderr only.** Tuner status is small and continuous by
design — see Decision 6.

---

## Decision 5 — agent authentication

A long-lived agent token, presented via SignalR's `AccessTokenProvider` and
validated by a `RequireAgent` policy.

The existing bearer infrastructure is built around Google ID tokens
([SignInEndpoints.cs](../TVRoom/Authorization/SignInEndpoints.cs)) and does not fit
a headless agent, so agents need their own credential type:

- Store a **hash** of the token, never the token, and compare in fixed time.
- A table mirroring `AuthorizedUser`
  ([AuthorizedUser.cs](../TVRoom/Persistence/AuthorizedUser.cs)), giving an admin
  add/revoke UI that follows the existing `UsersConfig` pattern.
- Revocation is deleting the row; the agent's next connect fails.

### Agent configuration in full

This is the deployment story, and the reason the per-broadcast passphrase costs
nothing:

```
server URL
agent token
tuner address
ffmpeg path
```

Nothing rotates, nothing is per-broadcast. Adding an agent is dropping in a token.
Channel, transcode parameters, and the SRT target and passphrase all arrive over
the control channel, so editing transcode config in the admin UI takes effect with
no agent redeploy.

---

## Decision 6 — the agent owns the tuner

`TunerClient` talks to `http://hdhomerun.local` for `lineup.json` and
`status.json`, which is reachable only from the residential network. So
`TunerClient`, `TunerStatusProvider`, and the `TunerAddress` configuration all
move to the agent. The server keeps its admin UI and endpoints, but they become
proxies over the control channel.

| Interaction | Today | After |
| --- | --- | --- |
| Channel lineup (`GET /channels`) | `TunerClient.GetAllChannelsAsync()` | Server→agent request/response, using SignalR client results |
| Tuner statuses | `TunerStatusProvider` polls 1/s | Agent pushes; already sketched as `IAgentProxy.GetTunerStatuses` |
| Resolve guide number → stream URL | `TunerClient.GetChannelAsync()` at broadcast start | **Stays entirely inside the agent** |

### Prerequisite: `ChannelInfo.Url` must stop crossing the boundary

This is worth fixing **before** the split, because it touches persistence and is
cheaper to change now than during a larger migration.

`ChannelInfo.Url` ([ChannelInfo.cs](../TVRoom/Tuner/ChannelInfo.cs)) comes from the
tuner's `lineup.json` and looks like `http://192.168.1.50:5004/auto/v5.1`. It is
ffmpeg's input, and it is meaningless outside the residential network — yet today
it travels well beyond it:

```
TunerClient.GetChannelAsync()          → ChannelInfo.Url = tuner stream URL
  BroadcastSessionFactory.cs:38        → CreateTranscode(channelInfo.Url)   ← ffmpeg -i
  BroadcastSession.cs:99   (restart)   → CreateTranscode(ChannelInfo.Url)   ← reuses stored URL
  BroadcastHistoryService.cs:24        → persists Url to BroadcastSessionRecord
  ControlPanelHub.cs:55                → GetLastChannel() rebuilds ChannelInfo from that row
  BroadcastApiEndpoints.cs:57          → OVERWRITES Url with the HLS playback URL
```

The field means "tuner input" internally and "playback URL" externally, and the
LAN-only value is persisted centrally along the way. That is already a smell; after
the split it is a correctness problem, because the server would store and replay a
URL that only resolves inside someone else's house.

**The tuner URL must never leave the agent.** The server addresses channels by
`GuideNumber`; the agent resolves it locally at transcode start. Consequences:

- `StartTranscodeCommand` carries `GuideNumber` and the transcode config, not a URL.
- The server's `ChannelInfo` becomes `(GuideNumber, GuideName)`, or `Url` is
  redefined as unambiguously the playback URL.
- `BroadcastSessionRecord.Url`
  ([BroadcastSessionRecord.cs](../TVRoom/Persistence/BroadcastSessionRecord.cs))
  drops or changes meaning — a migration. Guide number and name are sufficient for
  history.

One useful side effect: `RestartTranscodeAsync` currently reuses the URL captured
at broadcast start. Re-resolving from the guide number on the agent means a restart
picks up a **fresh** tuner URL, which is more robust if the tuner has reassigned
resources — plausibly relevant, since restart-on-hang is exactly the scenario that
feature exists for.

### Tuner status is not subject to the demand-driven rule

The demand-driven telemetry argument in Decision 4 is about **ffmpeg stderr**,
which is high-volume and bursty. Tuner status is eight small fields per tuner at
1 Hz — negligible on the uplink, and an unbroken history is wanted. **Push it
continuously.** Do not let the demand-driven rule be applied here by analogy.

**Keep the replay buffer on the server.** `TunerStatusProvider` currently holds
`Replay(61).RefCount()` locally
([TunerStatusProvider.cs:17-19](../TVRoom/Tuner/TunerStatusProvider.cs#L17-L19)).
In the split the agent should push raw samples and the server should hold the
61-sample window: it survives agent reconnects, and the admin UI is server-side
anyway.

That also removes a workaround. `BroadcastSession` currently takes a tuner-status
subscription it never uses, purely to stop `RefCount` tearing down the history
mid-broadcast. If the server owns the buffer there is no ref count to keep alive
and the subscription disappears.

### Agent-offline behaviour

Currently unspecified, and it needs to be:

- **Cache the lineup server-side, per agent.** It changes rarely, so the admin UI
  can render channels without a round trip and while the agent is down.
- **Tuner status simply stops.** The control panel needs an explicit "agent
  offline" state rather than a chart that silently stalls.

---

## The sharp edge: reconnection and split-brain

Design this first; it is where systems of this shape usually go wrong.

**The control channel and the media path are independent.** A control drop on a
residential connection does not stop video arriving over SRT.

- Server sees the connection close mid-broadcast. It must **not** tear down the
  broadcast — media is still flowing. `OnDisconnectedAsync` marks the agent
  disconnected and nothing more.
- Agent reconnects; `Reconnected` fires and it calls `ReportState` with its current
  state — *transcoding session X since T*.
- Server reconciles: still expecting X → resume; moved on → command a stop.

Without this you get split-brain, with the server believing nothing is running
while the agent pushes SRT into a listener that may have been reassigned.

**The nastier variant is a server restart.** Broadcast state is in-memory, so the
server returns with no memory of session X while the agent is still encoding.
Policy to decide: simplest is that an agent reporting an unknown session is told to
stop and the admin restarts the broadcast. Adopting an orphaned session would
require the port and passphrase to have survived, which they will not if generated
in memory.

---

## Relationship to existing work

- **[enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md)** — a
  duplex control channel *is* a remote mailbox. Agent events arrive as messages and
  queue into the server's broadcast loop exactly like local ones. Land the mailbox
  first: it should live on the **manager** side so the agent proxy stays a dumb
  transport, and `LocalAgent` on `refactor-for-agents` currently repeats the same
  unsynchronized `_currentSession` pattern that task exists to fix.
- **[enhancement-streaming-seam-types.md](enhancement-streaming-seam-types.md)** —
  `IAgentProxy` already returns `IAsyncEnumerable<T>`, which is the right seam
  type. Fix the `GetDebugOutput` regression noted there before that branch merges.
- **[known-issues.md](known-issues.md) issue 2** — the ingest endpoints stay
  loopback-only under this design, since the server-side remux writes to localhost.
  That rating only changes if the agent-ships-HLS option is ever revisited.
- **`refactor-for-agents` branch** — `IAgentProxy` and `LocalAgent` already sketch
  the proxy boundary, and `LocalAgent` remains the single-box deployment rather
  than a test double. The sketch predates the decisions here and needs three
  corrections — see [Where `IAgentProxy` fits](#where-iagentproxy-fits).

---

## Open questions

- **Multi-agent and multi-broadcast.** `BroadcastManager` enforces one broadcast at
  a time, and serving several households from one server means lifting that.
  Channel lineups are per-agent — two agents in different markets have entirely
  different channels — so multi-agent turns `GET /channels` into
  `GET /agents/{id}/channels` and `StartBroadcast(guideNumber)` into
  `StartBroadcast(agentId, guideNumber)`. Full analysis in
  [enhancement-multi-broadcast.md](enhancement-multi-broadcast.md), which is
  aspirational — **except for one schema choice that must ride along with item 0
  of the decomposition below**: if multi-agent is even plausible, add a nullable
  `AgentId` to `BroadcastSessionRecord` during that migration even though nothing
  reads it yet. An unused nullable column is close to free; a second migration on
  a table with live history is not.
- **Verify libsrt is present** in `lscr.io/linuxserver/ffmpeg:7.0.2` on both sides
  (`ffmpeg -protocols | grep srt`). If absent, RIST is the alternative, or
  TS-over-TCP with a reconnect wrapper. **Unverified.**
- **Verify the reverse proxy passes WebSocket** for the agent hub.
- **Partial segment policy** on mid-segment stream death (see Decision 2).
- **ABR ladder.** Once the server holds the stream, multiple renditions cost
  residential upload nothing — but that is real encode CPU on the server, a
  different cost profile from remux. Out of scope for the split itself.
- **Contribution bitrate vs uplink.** Transcode params are user-supplied; 1080p
  h264 is typically 4–8 Mbps. Confirm that fits the residential upload with the SRT
  latency buffer on top.

---

## Decomposition sketch

This document is too large to action directly. A plausible task breakdown, in
dependency order:

0. **Decouple the tuner URL** *(do first, standalone)* — stop `ChannelInfo.Url`
   crossing into persistence and restart, address channels by `GuideNumber`,
   migrate `BroadcastSessionRecord`. Shippable on its own against the current
   single-box deployment, and it removes a schema change from every task below.
1. **Agent identity** — token table, hashing, `RequireAgent` policy, admin UI.
2. **Control channel** — `AgentHub`, contracts assembly, agent-side connection,
   reconnect and `ReportState` reconciliation.
3. **Tuner proxying** — lineup request/response and status push over the control
   channel, server-side replay buffer and lineup cache, agent-offline UI state.
4. **Contribution path** — SRT listener lifecycle, per-broadcast passphrase and
   port allocation, server-side remux process, passphrase redaction.
5. **Agent host** — the deployable agent itself: tuner client, ffmpeg supervision,
   configuration.
6. **Demand-driven telemetry** — start/stop debug streaming on admin presence.
7. **Multi-broadcast** *(only if in scope)* — relax the single-broadcast
   constraint, port pool.

Item 0 is the only one that stands alone today; doing it first means the
`BroadcastSessionRecord` migration happens once. Items 1 and 2 come before 4,
since passphrase delivery depends on an authenticated control channel existing.
If multi-agent is in scope, fold its schema changes into item 0.

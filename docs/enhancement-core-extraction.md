# Enhancement — extract a framework-free core

**Status:** Proposed, not started · **Raised:** 2026-09-05

Decouple the HLS state machine and protocol code from ASP.NET, so it can be
tested and reused without a web host.

**Do this before rewriting the tests** — see [Sequencing](#sequencing).

---

## Framing

This is **not** a DDD domain layer, and building one here would be mostly
ceremony. TVRoom is a media server: most of the code is I/O orchestration,
protocol handling, and buffer lifetime.

What it does have is a real core worth isolating — **two state machines and a
protocol codec**:

- The HLS stream state machine (a sliding window of segments with sequence
  numbers and discontinuities)
- The broadcast lifecycle state machine — which does not exist yet as a thing;
  it is implicit in `BroadcastManager`'s mutable field, and would materialize in
  [enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md)
- Playlist parsing and serialization

Everything else is I/O.

Define the boundary by **what it excludes** — no ASP.NET, no EF, no ffmpeg, no
HTTP — rather than by dependency purism. `CommunityToolkit.HighPerformance` and
`ILogger` are fine.

---

## Phase 1 — cut the ASP.NET coupling

This is the valuable part and is independently shippable. The entanglement is
shallower than it looks: three signatures plus the result types.

```
HlsStreamState.cs        IResult ×12; ExecuteAsync(HttpContext) in 3 nested result types
ScopedBufferPool.cs:11   ReadToSharedBufferAsync(HttpRequest)
ScopedBufferPool.cs:19   RequestServices.GetRequiredService<ILogger<SharedBuffer>>()
HlsFileIngester.cs:23,29,35   Ingest*(HttpRequest)
MergedHlsLiveStream.cs:40,42,44   propagates IResult outward
```

### Playlists — write into a buffer, not a result

```csharp
// before
public abstract IResult GetMasterPlaylist();
public abstract IResult GetPlaylist();

// after
public abstract void WriteMasterPlaylist(IBufferWriter<byte> writer);
public abstract void WriteStreamPlaylist(IBufferWriter<byte> writer);
```

`PipeWriter` already implements `IBufferWriter<byte>`, so the endpoint still
passes `Response.BodyWriter` unchanged. The `audio/mpegurl` content type is a web
concern and moves to the endpoint.

### Segments — and this is where it pays off twice

```csharp
// before
public abstract IResult GetSegment(int index);

// after
public abstract bool TryGetSegment(int index, [MaybeNullWhen(false)] out IBufferLease lease);
```

**This is the same signature that issue 1 in
[known-issues-hls.md](known-issues-hls.md) needs.** That defect is that
`GetSegment` calls `Payload.Rent()`, which *throws* when the payload has been
disposed by eviction — so a viewer gets a 500 instead of a 404, on a window that
opens at every segment rotation. The fix proposed there is a `TryRent` that
returns `false` once disposed.

`TryGetSegment` returning `false` covers both "no such segment" and "payload
already disposed" with one signature. The decoupling makes the bug fix fall out
naturally rather than being bolted on, so **fix issue 1 as part of this work**.

### Buffer pool and ingester — take streams, not requests

```csharp
// before
public async Task<SharedBuffer> ReadToSharedBufferAsync(HttpRequest request)
public async Task IngestMasterPlaylist(HttpRequest request)

// after
public async Task<SharedBuffer> ReadToSharedBufferAsync(PipeReader body)
public async Task IngestMasterPlaylist(PipeReader body)
```

Take the `ILogger` in the constructor instead — `ScopedBufferPool` and
`HlsFileIngester` are both built by `TranscodeSession`, which already has one.
That removes the `RequestServices.GetRequiredService` service-location currently
buried inside a buffer pool method.

---

## Phase 2 — the project boundary (optional)

A `TVRoom.Core` project holding the state machines, parsers, playlist writer, and
value types, referenced by `TVRoom` and a future `TVRoom.Agent`.

**Its only added benefit is enforcement** — the compiler stops an ASP.NET
reference creeping back in, rather than relying on discipline. All the testability
value comes from phase 1. Do it when convenient, or when the agent split needs it.

When it happens, a narrower `TVRoom.Contracts` may want splitting out, since the
agent needs `ChannelInfo`/`TunerStatus`/`TranscodeStats` but not the HLS state
machine. See the trap below.

---

## What goes in, what stays out

| In | Out |
| --- | --- |
| `HlsStreamState` and subclasses | `FFmpegProcess`, `TranscodeSessionManager` |
| `HlsSegmentList`, `HlsSegmentEntry` | `TunerClient` |
| `ParsedMasterPlaylist`, `ParsedStreamPlaylist` | `TVRoomContext`, migrations, `BroadcastHistoryService` |
| Playlist serialization | `*ApiEndpoints`, `ControlPanelHub`, Razor pages |
| `TranscodeStats` + parsing | `HlsFileIngester` (I/O adapter — its correlation logic could move later) |
| `ChannelInfo`, `TunerStatus`, `HlsSegmentInfo` | `BroadcastSessionFactory` (composition) |

### Resist these

- **Repositories over `DbContext`.** It is already a unit of work plus
  repository. Four tables, trivial queries, and EF coupling is confined to five
  files that are all legitimately persistence-adjacent.
- **Interfaces for single implementations.** `IHlsFileIngester`,
  `ITranscodeSessionManager` are noise without a real seam. `IAgentProxy` is a
  genuine seam because it is about remoting, not testing; `ITunerClient` becomes
  genuine only when the agent proxies it.
- **Abstracting ffmpeg.** You would be mocking a process and testing the mock.
- **A domain events framework.** Rx plus the proposed mailbox already covers it.

### Ambiguous, and how to rule

- **`SharedBuffer` / `ScopedBufferPool`** are infrastructure by nature, but
  `HlsSegmentEntry` holds a `SharedBuffer`, so they come along. Do not fight this
  with generics or an `IPayload` abstraction; it buys nothing.
- **`BroadcastSession`** is conceptually the aggregate root but is implemented as
  an orchestrator owning disposables and Rx subjects. Split it: the rules (one
  active transcode; restart inserts a discontinuity; max duration stops it) are
  core; the disposal and subject plumbing are not.
- **`UserManager`** holds the one genuine business rule in the auth code — the
  configured admin email always gets both roles — welded to `DbContext` and
  `IConfiguration`. Low value to extract; note it and move on.

---

## Sequencing

**Before the test rewrite.** Issue 1 in [known-issues.md](known-issues.md) calls
for rewriting three test files that do not compile. Those tests target this API.
Rewriting them against the current `IResult`-coupled shape and *then* decoupling
means writing them twice.

**Before `TranscodeStats` becomes an agent contract.** Once a type crosses the
agent wire it is versioned and its field names are frozen.
[enhancement-ffmpeg-progress.md](enhancement-ffmpeg-progress.md) proposes
reshaping `TranscodeStats` — per-stream `q`, `out_time_us` instead of the `size=`
that is currently parsed and discarded. Do that reshaping before
[architecture-server-agent-split.md](architecture-server-agent-split.md) freezes
it. Note it already carries `[JsonPropertyName]` attributes because it is
serialized to the browser, so it is a wire type in one direction already.

---

## Verification

The acceptance criterion is concrete: **a test can construct a
`HlsStreamWithSegments`, push segments through it, and assert on the emitted
playlist bytes — with no web host.** That is impossible today.

## Risks

- **There is no test safety net while doing this**, because the test project does
  not compile. Two options: minimally fix the tests first for a net and accept
  rewriting them, or do the refactor unprotected and write tests against the clean
  API afterwards. The refactor is mechanical and compiler-checked, so the second is
  reasonable — but verify manually against the running app that playlists and
  segments still serve, since a playlist byte-level regression would not surface
  as a compile error.
- **It touches code with open defects.** Issue 1 converges with this work and
  should be fixed here. Issues 3 and 5 in the HLS list are in adjacent code but are
  separate changes — do not fold them in.

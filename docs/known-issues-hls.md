# Known issues — HLS pipeline

Open items from the in-depth review of `TVRoom/HLS/` on 2026-09-03. All are still
open; nothing in this list has been fixed.

The refcounting design is sound. `SharedBuffer` plus a lease plus the
`ScopedBufferPool` sweep is a good shape, and renting the buffer *before*
returning the `IResult` from `GetSegment` correctly prevents eviction from
pulling a buffer out from under an in-flight response. The problems below are
about **ordering** and **teardown**, not the core scheme.

---

## 1. Evicted payloads are disposed before the state referencing them is replaced

**Severity:** Medium · **Area:** [HlsStreamState.cs:79](../TVRoom/HLS/HlsStreamState.cs#L79),
[MergedHlsLiveStream.cs:23-24](../TVRoom/HLS/MergedHlsLiveStream.cs#L23-L24)

`WithNewSegment` disposes the evicted payload while it is still building the new
state:

```csharp
newPreviousSegments = PreviousSegments.Push(moveToPrevious, out var toBeDisposed);
toBeDisposed?.Payload.Dispose();
```

The caller only publishes afterwards:

```csharp
var newState = _streamStates.Value.WithNewSegment(segmentInfo);  // dispose happens in here
_streamStates.OnNext(newState);                                   // swap happens after
```

Between those two lines `_streamStates.Value` is still the *old* state, whose
`PreviousSegments` contains the segment just disposed. A concurrent `GetSegment`
that reads that state finds the entry and calls `segment.Payload.Rent()`
([HlsStreamState.cs:103](../TVRoom/HLS/HlsStreamState.cs#L103)) — and `Rent()`
throws rather than degrading, because
[SharedBuffer.cs:90](../TVRoom/HLS/SharedBuffer.cs#L90) is
`ObjectDisposedException.ThrowIf(_disposed, this)`.

The viewer gets a 500 from `UseExceptionHandler`, not a 404. The window is
narrow, but it opens on every segment rotation (roughly every 2s) for the life of
every broadcast, against however many viewers are polling.

**Fix:** reordering to publish-then-dispose shrinks the window but does not close
it — a request that already captured the snapshot can still lose the race. Do
both:

1. Add a `TryRent(out IBufferLease)` that returns `false` once `_disposed` is
   set, and have `GetSegment` map that to `Results.NotFound()`.
2. Return the evicted entry from `WithNewSegment` and dispose it after
   `OnNext`.

---

## 2. `ScopedBufferPool` disposal race leaks a buffer

**Severity:** Medium · **Area:** [ScopedBufferPool.cs:15](../TVRoom/HLS/ScopedBufferPool.cs#L15),
[ScopedBufferPool.cs:37](../TVRoom/HLS/ScopedBufferPool.cs#L37)

The disposed check and the registration are separated by an `await` on the
request body:

```csharp
lock (_lock) { ObjectDisposedException.ThrowIf(_disposed, this); }
// ... await reader.ReadAsync() ...
_liveBuffers.TryAdd(sharedBuffer.Id, sharedBuffer);
```

If `Dispose()` runs in that gap it enumerates a snapshot that does not include
this buffer, and nothing ever disposes it. It is then reclaimed only by the
finalizer, which logs a warning — and trips `Debug.Fail` in Debug builds.

This is not theoretical. Stopping a broadcast disposes the pool while ffmpeg may
still have an ingest PUT in flight, which is the normal shutdown sequence.

**Fix:** re-check `_disposed` after `TryAdd` and dispose the buffer if the pool
closed underneath, or perform the create-and-add under the lock.

(For the record, the mutation-during-enumeration in `Dispose` is fine —
`ConcurrentDictionary.Values` returns a snapshot.)

---

## 3. `IsReady` returns true when `Ready` did not succeed

**Severity:** Low-medium · **Area:** [MergedHlsLiveStream.cs:32](../TVRoom/HLS/MergedHlsLiveStream.cs#L32)

```csharp
public bool IsReady => Ready.IsCompleted;
```

`IsCompleted` is also true for **faulted** and **canceled** tasks. `Ready` is
`Skip(1).Take(N).ToTask()` ([line 27](../TVRoom/HLS/MergedHlsLiveStream.cs#L27))
and `Dispose()` completes the subject, so a broadcast torn down before any state
arrives leaves `Ready` faulted — and `IsReady` then reports `true`.
`IsCompletedSuccessfully` is what is meant.

Two related consequences:

- `BroadcastManager` does `_ = NotifyWhenReady(session)`, so that fault becomes
  an unobserved task exception.
- If exactly one state arrives before teardown, `Take(2)` completes
  *successfully* with one element, so `BroadcastReady` can fire below
  `HlsPlaylistReadyCount`.

---

## 4. Playlist bytes depend on the source file line endings

**Severity:** Low (latent) · **Area:** [HlsStreamState.cs](../TVRoom/HLS/HlsStreamState.cs)

Verified rather than assumed: a C# raw string literal inherits the line endings
of its source file. The same literal compiles to 24 bytes from an LF source and
25 bytes from a CRLF source.

The playlist headers in `MasterPlaylistResult.WriteTo` and
`StreamPlaylistResult.WriteTo` are raw string literals, so they emit `\n` today.
Segment entries hardcode `\r\n` in `HlsSegmentEntry.WriteTo`. The response is
therefore mixed-terminator.

Parsers tolerate both, so this is latent rather than broken, but it is an
invisible coupling between checkout settings and wire output.

**Partly addressed.** A `.gitattributes` with `* text=auto eol=lf` now pins the
working tree to LF on every platform, so a CRLF checkout can no longer change the
bytes on the wire, and a Windows build matches the Linux Docker build. See
*Already addressed* in [README.md](README.md).

**Still open:** the response remains mixed-terminator, and the header half still
depends on the source file rather than saying what it means. Write the terminators
explicitly instead of relying on the literal.

---

## 5. Lost update on `_streamStates` during transcode restart

**Severity:** Low · **Area:** [MergedHlsLiveStream.cs:36](../TVRoom/HLS/MergedHlsLiveStream.cs#L36)

`SetNewSource` performs a read-modify-write:

```csharp
_streamStates.OnNext(_streamStates.Value.WithNewDiscontinuity());
```

with no synchronization against the identical pattern in the ingest subscriber at
line 23. `RestartTranscodeAsync` disposes the old session before calling
`SetNewSource`, but `ProcessIngestedFiles` drains its channel asynchronously, so
both can run concurrently.

Whichever update loses drops either the `#EXT-X-DISCONTINUITY` marker or a whole
segment — and a dropped segment payload is then unreferenced, surviving only
until the pool sweep at the end of the broadcast.

**Fix:** serialize the read-modify-write locally — a lock around the transition,
or a compare-exchange retry loop. Keep it inside `MergedHlsLiveStream`.

**Not fixed by the mailbox.** The lifecycle task in
[enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md)
serializes commands against each other, which puts `SetNewSource` on the loop
thread — but the competing writer is the Rx ingest subscriber running on the
`HlsFileIngester` channel-consumer thread, which that loop does not touch.

---

## 6. Smaller items

**`TargetDuration` changes type mid-pipeline.** `HlsSegmentInfo.TargetDuration`
is `int` but `HlsStreamWithSegments.TargetDuration` is `double`
([HlsStreamState.cs:57](../TVRoom/HLS/HlsStreamState.cs#L57)). RFC 8216 requires
`#EXT-X-TARGETDURATION` to be an integer; the output only stays valid because the
value originates as an `int`. Make it `int` end to end.

**Parsers over-allocate by 2x.** Both parsers rent `payload.Length * 2` chars
([ParsedMasterPlaylist.cs:26](../TVRoom/HLS/ParsedMasterPlaylist.cs#L26),
[ParsedStreamPlaylist.cs:30](../TVRoom/HLS/ParsedStreamPlaylist.cs#L30)). The
maximum number of chars decoded from N UTF-8 bytes is N, so this doubles a
rental that can reach 10 MB.

**Oversize bodies return 500, not 413.** The guard at
[ScopedBufferPool.cs:29](../TVRoom/HLS/ScopedBufferPool.cs#L29) throws
`InvalidOperationException`.

**`default(HlsSegmentList)` has a null `_items`** and will throw a
`NullReferenceException` on `Push`. Not reachable today, but the struct is
public.

**No `#EXT-X-DISCONTINUITY-SEQUENCE` header** is emitted despite emitting
`#EXT-X-DISCONTINUITY`. Some players need it to resync after a restart.

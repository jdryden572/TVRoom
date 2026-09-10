# Enhancement — standardize streaming types at component seams

**Status:** Proposed, not started · **Raised:** 2026-09-04

Settle which streaming type crosses component boundaries, and fix one place where
the inconsistency has already produced a bug.

Worth doing **before** the `refactor-for-agents` branch merges, since that branch
is where the seam becomes load-bearing.

---

## The rule

```
IObservable<T>  →  bounded Channel<T>  →  ReadAllAsync()  →  IAsyncEnumerable<T>
   internal          internal                                    public
   composition       buffering + drop policy                     contract
```

- **Rx stays internal**, for composition — `Switch`, `Replay`, `Merge`, `Sample`.
- **Channels stay internal**, as work queues and as the buffering strategy behind
  an adapter. The drop policy lives here.
- **`IAsyncEnumerable<T>` is what components expose.**

Nothing about channels goes away. They stop being *the type you hand out* and
become the buffering strategy behind it.

## What this does and does not touch

Three distinct uses of `Channel<T>` exist in this codebase, and only the third is
in scope.

**1. Internal work queue — keep, and expand.**
`HlsFileIngester._channel` is a bounded `Channel<IngestHlsFile>(10)` connecting
HTTP ingest threads to a single consumer loop
([HlsFileIngester.cs](../TVRoom/HLS/HlsFileIngester.cs)). Real backpressure, one
consumer, never exposed. The mailbox proposed in
[enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md) is the
same pattern. Do not change these.

**2. Internal composition — keep Rx.**
`FFmpegProcess.FFmpegOutput`, `BroadcastSession.DebugOutput`,
`MergedHlsLiveStream._sources.Switch()`, `TunerStatusProvider.Statuses`. These
genuinely need operators Rx is good at. Do not change these.

**3. Boundary return types — this is the seam.**
`ControlPanelHub` returns `ChannelReader<T>` from three methods
([ControlPanelHub.cs:71](../TVRoom/Broadcast/ControlPanelHub.cs#L71),
[:84](../TVRoom/Broadcast/ControlPanelHub.cs#L84),
[:87](../TVRoom/Broadcast/ControlPanelHub.cs#L87)), and `IAgentProxy` on
`refactor-for-agents` returns `IAsyncEnumerable<T>`.
[ObservableToChannelExtensions.cs](../TVRoom/Broadcast/ObservableToChannelExtensions.cs)
is the adapter that exists only to satisfy SignalR.

## Why `IAsyncEnumerable<T>` at the seam

**Remoting.** `IAgentProxy` exists so the transcode can move to another machine.
`ChannelReader<T>` has no wire representation and would need unwrapping at every
transport. `IAsyncEnumerable<T>` is what gRPC server-streaming and SignalR both
map onto directly.

**Teardown.** `AsChannelReader` makes cancellation the *only* unsubscribe
mechanism — the caller must pass a token, and `unsubscribe.Register(...)` is what
disposes the Rx subscription. With `IAsyncEnumerable<T>` and
`[EnumeratorCancellation]`, breaking out of `await foreach` disposes the
enumerator and tears the subscription down deterministically, with no token
threaded by hand.

**SignalR no longer requires channels.** Hub methods have accepted
`IAsyncEnumerable<T>` since ASP.NET Core 3.0, so `ObservableToChannelExtensions`
solves a problem that no longer exists. It can become an internal detail of the
adapter rather than a public conversion.

---

## The regression this has already caused

On branch `refactor-for-agents` (commit `6bd0826`), `TVRoom/Agent/LocalAgent.cs`
has all three telemetry methods side by side, and one of them is wrong:

```csharp
// line 76 — GetDebugOutput
return _currentSession.DebugOutput.ToAsyncEnumerable();

// line 86 — GetTranscodeStats
return _currentSession.TranscodeStats.AsChannelReader(cancellation).ReadAllAsync(cancellation);

// line 91 — GetTunerStatuses
return _tunerStatusProvider.Statuses.AsChannelReader(cancellation).ReadAllAsync(cancellation);
```

`GetDebugOutput` has two defects that the other two do not:

1. **It drops the backpressure policy.** Rx is push-based with no backpressure, so
   converting to a pull model forces a buffering decision. `AsChannelReader` makes
   that decision explicitly — capacity 200, `BoundedChannelFullMode.DropOldest` —
   which is the right call for an admin browser watching ffmpeg stderr.
   `AsyncEnumerableEx.ToAsyncEnumerable(IObservable<T>)` has a single overload
   taking only the source: no capacity, no drop mode (verified against the shipped
   XML docs for `System.Interactive.Async` 7.0.0). The policy cannot be expressed
   through it, so a slow or stalled consumer buffers instead of dropping.

   Debug output is the *worst* stream to lose this on: it is the highest-volume of
   the three, and it is highest-volume precisely when a transcode is failing.

2. **It ignores its `cancellation` parameter entirely**, so the Rx subscription is
   never torn down when the consumer goes away.

The fix is to match the two methods below it. This is an inconsistency inside one
file, not a design disagreement — the correct pattern is already there.

---

## Implementation steps

1. Fix `LocalAgent.GetDebugOutput` on `refactor-for-agents` to use
   `.AsChannelReader(cancellation).ReadAllAsync(cancellation)`. Do this before the
   branch merges.
2. Decide whether `ControlPanelHub` should return `IAsyncEnumerable<T>` instead of
   `ChannelReader<T>`. Behaviourally equivalent for SignalR; the value is having
   one seam type across hub and agent.
3. If yes, fold `AsChannelReader` into an adapter that returns
   `IAsyncEnumerable<T>` — keeping the bounded channel and its drop policy inside
   — and use `[EnumeratorCancellation]` so `await foreach` teardown works without
   an explicit token.
4. Delete `WriteToChannelObserver<T>`
   ([WriteToChannelObserver.cs](../TVRoom/Broadcast/WriteToChannelObserver.cs)) if
   the lifecycle task has not already; it is referenced nowhere and duplicates
   `AsChannelReader`.

## Risks and notes

- Keep the drop policy explicit and reviewable wherever it ends up. The failure
  mode of getting this wrong is unbounded memory growth under exactly the
  conditions where you most want telemetry, and it is silent.
- The three streams may not want the same policy. `DropOldest` suits debug output
  and stats; for tuner statuses, dropping samples creates the history gaps that
  `TunerStatusProvider` already goes out of its way to avoid. Worth deciding
  per-stream rather than inheriting one shared `BoundedChannelOptions` — the
  current adapter applies the same options to all three.
- This is orthogonal to the mailbox work and can ship independently, but both
  touch `LocalAgent`, so sequence them to avoid conflicts.

# Enhancement — serialize broadcast lifecycle behind a mailbox

**Status:** Proposed, not started · **Raised:** 2026-09-04

Give the broadcast lifecycle a single owner, so that transitions are serialized
and cleanup is guaranteed rather than dependent on an unobserved task.

Independent of [enhancement-ffmpeg-progress.md](enhancement-ffmpeg-progress.md);
either can ship first.

---

## Problem

### One subject wearing three hats

`BroadcastSession._transcodeSessions` is a single `BehaviorSubject<TranscodeSession>`
doing three unrelated jobs:

1. **Current-value holder** — `TranscodeSession => _transcodeSessions.Value`
   ([BroadcastSession.cs:114](../TVRoom/Broadcast/BroadcastSession.cs#L114))
2. **Switching source** for telemetry — `.Select(s => s.FFmpegOutput).Switch()`
3. **Lifecycle completion signal** — `Finished => _transcodeSessions.ToTask()`
   ([BroadcastSession.cs:79](../TVRoom/Broadcast/BroadcastSession.cs#L79))

The third is the problem. "This broadcast has ended" travels as `OnCompleted()`
on a subject whose *values* are transcode sessions, and `ToTask()` returns the
last session only to discard it. Nothing in the type says that completing this
subject is what ends the broadcast, yet `StopAsync`, `Dispose`, and `_cleanup`
all touch it for different reasons.

### No single owner of transitions

`BroadcastManager._currentSession`
([BroadcastManager.cs:14](../TVRoom/Broadcast/BroadcastManager.cs#L14)) is written
in `StartSession` ([line 44](../TVRoom/Broadcast/BroadcastManager.cs#L44)), cleared
inside a discarded task ([line 64](../TVRoom/Broadcast/BroadcastManager.cs#L64)),
and read from request threads ([lines 28-31](../TVRoom/Broadcast/BroadcastManager.cs#L28-L31))
with no synchronization. Two consequences that matter:

- **Cleanup lives in an unobserved task.**
  [`_ = NotifyWhenStopped(session)`](../TVRoom/Broadcast/BroadcastManager.cs#L48)
  is the only thing that disposes the session and clears `_currentSession`. If
  `session.Dispose()` throws, `_currentSession` is never cleared and **no further
  broadcast can start** — `StartSession` throws "another is already active"
  until the process restarts.
- **Double-dispose throws during shutdown.** `BroadcastSession` has no
  `_disposed` guard, and its `Dispose()` reads `_transcodeSessions.Value`, which
  throws once the subject is disposed. `NotifyWhenStopped` racing
  `BroadcastManager.Dispose()`
  ([line 87](../TVRoom/Broadcast/BroadcastManager.cs#L87)) produces an
  `ObjectDisposedException` out of DI container disposal.

There is also a plain check-then-act race in `StartSession`: the
`_currentSession is not null` guard at
[line 35](../TVRoom/Broadcast/BroadcastManager.cs#L35) and the assignment at line
44 are not atomic.

And a fourth writer that is easy to miss: the max-duration auto-stop fires from a
`CancellationToken.Register` callback and calls `Task.Run(StopAsync)`
([BroadcastSession.cs:56-60](../TVRoom/Broadcast/BroadcastSession.cs#L56-L60)) —
a timer thread mutating broadcast state with no coordination against the hub
thread or the cleanup continuation.

### The states exist but live in three unrelated mechanisms

There is already a state machine here; it is just not written down anywhere:

| Conceptual state | How it is currently encoded |
| --- | --- |
| Idle vs. active | `_currentSession is null` |
| Ready | `HlsLiveStream.IsReady`, i.e. `Ready.IsCompleted` — which is also true for a **faulted** task, issue 3 in [known-issues-hls.md](known-issues-hls.md) |
| Restarting | not represented at all |

A nullable field, a `Task`'s completion status, and Rx subject lifetimes. Nothing
can be reasoned about in one place, and illegal transitions are prevented — where
they are prevented — incidentally rather than explicitly.

---

## Design: a single mailbox loop

This shape is already used in `HlsFileIngester` — a bounded `Channel<T>` with one
consumer loop — so it is idiomatic here rather than a new concept.

Make commands and events into messages on one channel, with a single loop that
owns the current session and performs every transition. The key move is that the
session becomes a **local variable inside the loop**, not a field — once it is
confined to one thread there is no shared memory left to reason about.

Reads stay lock-free: the loop publishes an immutable snapshot to one field, and
`CurrentSession` / `NowPlaying` read that field rather than mutable session state.

### The state machine

Serialization alone would only switch on message type, leaving "is the session
null" as the whole model. Make the states explicit so transitions are total and
illegal combinations are rejected deliberately.

**States:** `Idle`, `Starting`, `Ready`, `Restarting`, `Stopping`

**Commands** (from an admin): `Start(ChannelInfo)`, `Stop`, `RestartTranscode`

**Events** (from the system): `StreamReady`, `RestartCompleted`,
`TranscodeEnded`, `MaxDurationReached`

| From | Message | To | Action |
| --- | --- | --- | --- |
| Idle | `Start` | Starting | create session, spawn ffmpeg |
| Idle | `Stop`, `Restart` | Idle | ignore |
| Starting | `StreamReady` | Ready | notify `BroadcastReady` |
| Starting | `TranscodeEnded` | Idle | failed to start; dispose |
| Starting | `Stop` | Stopping | |
| Starting | `Start` | Starting | reject — already active |
| Ready | `Restart` | Restarting | stop old transcode, insert discontinuity, start new |
| Ready | `Stop` | Stopping | |
| Ready | `MaxDurationReached` | Stopping | |
| Ready | `TranscodeEnded` | Idle | unexpected exit; dispose |
| Ready | `Start` | Ready | reject — already active |
| Restarting | `RestartCompleted` | Ready | new transcode started |
| Restarting | `TranscodeEnded` | Idle | restart failed |
| Restarting | `Stop` | Stopping | |
| Stopping | `TranscodeEnded` | Idle | dispose, notify `BroadcastStopped` |

`Restarting` needs its own completion event rather than reusing `StreamReady`:
`MergedHlsLiveStream.Ready` is a one-shot `Task` created in the constructor, so it
will not fire again after `SetNewSource`. Note `RestartCompleted` means "the new
transcode started", not "new segments are flowing" — waiting for the first segment
from the new source would be stricter, but no such signal exists today.

Three things this table makes explicit that the current code does not:

- **Viewer availability is derived, not a state.** The machine describes the
  *transcode* lifecycle. Viewers continue to be served throughout `Restarting` —
  that is the entire point of the merge feature — so `NowPlaying` gates on
  `state is Ready or Restarting`, replacing today's `IsReady` check and its
  faulted-task bug.
- **`Starting` must exist for the loop to stay responsive.** The loop is
  sequential, so a long `await` inside one message blocks every queued message
  behind it; `CreateBroadcast` spawns ffmpeg and can take seconds. Keep long work
  outside the loop: `Start` moves to `Starting` and kicks the work off without
  awaiting, and completion arrives later as its own message. Without a `Starting`
  state there is nowhere to park that.
- **`MaxDurationReached` becomes a message**, so the auto-stop timer stops being
  an uncoordinated fourth writer.

### No `Faulted` state — log transitions with reasons instead

**Decided 2026-09-05.** A failure is not a distinct state; it is a *reason* for
entering `Idle`. Adding `Faulted` would fork every transition that can fail and
buy nothing the log does not.

**Reasons ride on messages, not states.** The message is what carries the
"why", so the state stays a plain enum and the reason travels with the trigger:

```csharp
record Stop(StopReason Reason);              // AdminRequested | MaxDuration
record TranscodeEnded(bool Expected);        // expected exit vs. ffmpeg died
record Restart(RestartReason Reason);        // AdminRequested | (future) AutoRecovery
```

The loop appends one entry per transition: timestamp, from-state, to-state,
trigger, and reason where applicable. That gives admins the whole story —
*"Ready → Idle, TranscodeEnded, unexpected"* answers "why did it stop?" without a
state dedicated to it.

Volume is a handful of entries per broadcast, so this is cheap. Start with an
in-memory ring buffer surfaced on the control panel. Persisting it is a natural
extension and genuinely useful — the interesting question is usually "why did it
stop last night?" — but it wants a table, so fold it into the
`BroadcastSessionRecord` migration in Decision 6 of
[architecture-server-agent-split.md](architecture-server-agent-split.md) rather
than doing a migration of its own.

### Where it lives: the manager, not the session

The state machine belongs in `BroadcastManager`. The deciding argument is
`Idle`: **in `Idle` there is no session**, so a session-owned machine has nowhere
to put its most important state.

Model it as a discriminated union where the non-`Idle` states carry the session:

```csharp
abstract record BroadcastState;
sealed record Idle                                  : BroadcastState;
sealed record Starting(BroadcastSession Session)    : BroadcastState;
sealed record Ready(BroadcastSession Session)       : BroadcastState;
sealed record Restarting(BroadcastSession Session)  : BroadcastState;
sealed record Stopping(BroadcastSession Session)    : BroadcastState;
```

That kills the `_currentSession?.` null-checking scattered through the manager
today ([BroadcastManager.cs:28-31](../TVRoom/Broadcast/BroadcastManager.cs#L28-L31),
[:71](../TVRoom/Broadcast/BroadcastManager.cs#L71),
[:79](../TVRoom/Broadcast/BroadcastManager.cs#L79)) — the compiler stops you
reaching for a session in a state that does not have one.

The resulting split:

| | Owns |
| --- | --- |
| `BroadcastManager` | the mailbox, the state machine, the single-broadcast invariant, the transition log |
| `BroadcastSession` | the transcode session(s), HLS live stream, buffer pool — a **resource** the loop drives |

`BroadcastSession` keeps stream composition (the `_transcodeSessions` switching
that makes restart-without-disconnect work — that is not lifecycle) but gives up
lifecycle decisions. Its internal signals become messages posted to the manager's
mailbox: `HlsLiveStream.Ready` becomes `StreamReady`, `Finished` becomes
`TranscodeEnded`, and the auto-stop timer becomes `MaxDurationReached` instead of
`Task.Run(StopAsync)`.

This also lines up with the agent split, where the manager holds the loop and
`IAgentProxy` stays a dumb transport — see
[architecture-server-agent-split.md](architecture-server-agent-split.md).
`LocalAgent` on `refactor-for-agents` currently keeps its own `_currentSession`,
which under this design should not exist at all.

### What this fixes

- The `StartSession` check-then-assign race
- `_currentSession` visibility across threads
- Cleanup becomes `try`/`finally` inside the loop, not a task nobody awaits
- Start, Stop, and Restart can no longer interleave with each other, so two admin
  connections issuing commands at once is safe
- The lifecycle becomes readable in one function instead of scattered across
  fire-and-forget continuations

### What this does *not* fix

An earlier draft claimed the mailbox also fixes the `_streamStates` lost update
(issue 5 in [known-issues-hls.md](known-issues-hls.md)). **It does not.**

The mailbox serializes *commands* against each other, so `SetNewSource` would run
on the loop thread. But the competing writer is the Rx ingest subscriber in
`MergedHlsLiveStream`, which runs on the `HlsFileIngester` channel-consumer
thread — a different loop entirely. Serializing commands does nothing about it.

Routing segment arrivals through this mailbox *would* close the race, but that is
the wrong trade: it puts 2-second-cadence media events behind control commands in
one queue. Issue 5 wants its own fix local to `MergedHlsLiveStream` — a lock or a
compare-exchange retry around the read-modify-write.

### Then simplify the signals

- `Finished` becomes a `TaskCompletionSource` or `CancellationToken` that says
  what it means; `_transcodeSessions` goes back to doing only job 2.
- Add a `_disposed` guard to `BroadcastSession`.

---

## Component: one status object

Broadcast state currently reaches clients three ways:

- Hub callbacks `BroadcastStarted` / `BroadcastReady` / `BroadcastStopped`
  ([ControlPanelHub.cs](../TVRoom/Broadcast/ControlPanelHub.cs))
- A hub pull, `GetCurrentSession()`
- REST polling of `/broadcast/current`
  ([BroadcastApiEndpoints.cs:49](../TVRoom/Broadcast/BroadcastApiEndpoints.cs#L49))

Three code paths for one concept, and every new client has to wire all three.

Collapse to a single `BroadcastStatus` record (state, channel, sessionId,
startedAt, ready) that the loop publishes on each transition. The hub streams it,
`GetCurrentSession()` returns the latest snapshot, and `/broadcast/current`
projects the same snapshot.

This is separable from the mailbox and can ship after it, but it depends on the
loop existing to have one place that owns transitions.

**Watch the audience difference.** The three paths being collapsed do not have
the same authorization: the hub is `[Authorize(Policies.RequireAdministrator)]`,
while `/broadcast/current` requires only `RequireApiViewer`. One record served to
both means anything added to it is visible to every authorized viewer. This is
already a live constraint —
[enhancement-viewer-presence.md](enhancement-viewer-presence.md) is admin-only by
decision, so its data must not land on the shared object without a separate
admin-only projection.

---

## Also in scope

Small items in the same code paths, worth folding in rather than filing
separately:

- **Broadcast history is identified by a heuristic.**
  `EndCurrentBroadcast` ([BroadcastHistoryService.cs:40](../TVRoom/Broadcast/BroadcastHistoryService.cs#L40))
  closes "the latest row by Id, if `EndedAt` is null" rather than the row this
  session actually created. Have `StartNewBroadcast` return the record id, hold
  it on the session, and close by id. Add a startup sweep for rows orphaned by a
  crash — those currently stay open forever, which also skews `GetLatestBroadcast`.
- **Inverted ownership.** `TranscodeSession.Dispose()` calls
  `_transcodeManager.Remove(Id)`
  ([TranscodeSession.cs:81](../TVRoom/Transcode/TranscodeSession.cs#L81)) — the
  child unregisters itself from the manager that created it and holds it in a
  dictionary. Let the manager own removal.
- **Delete `WriteToChannelObserver<T>`**
  ([WriteToChannelObserver.cs](../TVRoom/Broadcast/WriteToChannelObserver.cs)). It
  is referenced nowhere and duplicates `AsChannelReader`.

---

## Related: the seam type

Which streaming type crosses component boundaries is tracked separately in
[enhancement-streaming-seam-types.md](enhancement-streaming-seam-types.md). It is
orthogonal to this work, but both tasks touch `LocalAgent`, so sequence them to
avoid conflicts.

One point belongs here rather than there: `LocalAgent` on `refactor-for-agents`
repeats the same unsynchronized `_currentSession` pattern this task exists to
fix. The mailbox should live on the **manager** side so the proxy stays a dumb
transport, and so this is fixed in one place rather than two.

## Risks

- The mailbox changes the failure mode of `StartSession` from a thrown exception
  to a queued command; decide whether callers get a result back (a
  `TaskCompletionSource` per command keeps the current request/response shape).
- Hub methods currently call into `BroadcastManager` synchronously and await the
  result. Preserve that so the control panel still gets errors inline.

---

## Reference implementation sketch

Illustrative, not prescriptive — written against the current types to show the
shape. Skip to [Decisions embedded above](#decisions-embedded-above) for the parts
that are load-bearing.

### State

```csharp
// Non-Idle states carry the session, so the compiler stops you reaching for
// one that doesn't exist. Private ctor seals the hierarchy for exhaustiveness.
public abstract record BroadcastState
{
    private BroadcastState() { }

    public sealed record Idle : BroadcastState;
    public sealed record Starting(BroadcastSession Session) : BroadcastState;
    public sealed record Ready(BroadcastSession Session) : BroadcastState;
    public sealed record Restarting(BroadcastSession Session) : BroadcastState;
    public sealed record Stopping(BroadcastSession Session) : BroadcastState;

    public BroadcastSession? SessionOrNull => this switch
    {
        Starting s   => s.Session,
        Ready r      => r.Session,
        Restarting r => r.Session,
        Stopping s   => s.Session,
        _            => null,
    };
}
```

### Messages

```csharp
public enum StopReason { AdminRequested, MaxDuration, TranscodeFailed }
public enum RestartReason { AdminRequested }

public abstract record BroadcastMessage
{
    private BroadcastMessage() { }

    // Commands — carry a reply so the hub keeps its inline error reporting
    public sealed record Start(string GuideNumber,
                               TaskCompletionSource<BroadcastInfo> Reply) : BroadcastMessage;
    public sealed record Stop(StopReason Reason, TaskCompletionSource Reply) : BroadcastMessage;
    public sealed record Restart(RestartReason Reason, TaskCompletionSource Reply) : BroadcastMessage;

    // Events — SessionId tags every event so a late signal from a torn-down
    // session can't drive a transition on the one that replaced it.
    public sealed record StreamReady(string SessionId) : BroadcastMessage;
    public sealed record RestartCompleted(string SessionId) : BroadcastMessage;
    public sealed record TranscodeEnded(string SessionId, bool Expected) : BroadcastMessage;
    public sealed record MaxDurationReached(string SessionId) : BroadcastMessage;
}
```

### Log and snapshot

```csharp
public sealed record TransitionEntry(
    DateTimeOffset At, string From, string To, string Trigger,
    string? Reason, string? SessionId);

public sealed record BroadcastSnapshot(string State, BroadcastInfo? Info, DateTimeOffset Since)
{
    public static readonly BroadcastSnapshot Idle =
        new(nameof(BroadcastState.Idle), null, DateTimeOffset.UtcNow);
}
```

### The manager

```csharp
public sealed partial class BroadcastManager : IAsyncDisposable
{
    private readonly Channel<BroadcastMessage> _mailbox =
        Channel.CreateUnbounded<BroadcastMessage>(new() { SingleReader = true });

    private readonly ConcurrentQueue<TransitionEntry> _log = new();   // bounded on write
    private volatile BroadcastSnapshot _snapshot = BroadcastSnapshot.Idle;
    private readonly Task _loop;

    public BroadcastManager(/* factory, hub, tuner client, config, logger */)
        => _loop = Task.Run(RunAsync);

    // Lock-free reads. Replaces CurrentSession / NowPlaying.
    public BroadcastSnapshot Current => _snapshot;
    public BroadcastInfo? NowPlaying => _snapshot.State is "Ready" or "Restarting"
        ? _snapshot.Info : null;
    public IReadOnlyCollection<TransitionEntry> Transitions => _log.ToArray();

    public Task<BroadcastInfo> StartAsync(string guideNumber)
    {
        // RunContinuationsAsynchronously matters: without it the caller's
        // continuation runs on the loop thread and stalls the mailbox.
        var reply = new TaskCompletionSource<BroadcastInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _mailbox.Writer.TryWrite(new BroadcastMessage.Start(guideNumber, reply));
        return reply.Task;
    }

    private void Post(BroadcastMessage m) => _mailbox.Writer.TryWrite(m);

    public async ValueTask DisposeAsync()
    {
        _mailbox.Writer.TryComplete();
        await _loop;                    // loop disposes whatever it still holds
    }
}
```

### The loop

```csharp
private async Task RunAsync()
{
    BroadcastState state = new BroadcastState.Idle();

    await foreach (var message in _mailbox.Reader.ReadAllAsync())
    {
        var before = state;
        try
        {
            state = await ApplyAsync(state, message);
        }
        catch (Exception ex)
        {
            // A throwing transition must never wedge the manager. Today a
            // throwing Dispose() leaves _currentSession set forever.
            LogTransitionFailed(ex, before.GetType().Name, message.GetType().Name);
            SafeDispose(before.SessionOrNull);
            state = new BroadcastState.Idle();
        }

        if (!ReferenceEquals(before, state))
        {
            Record(before, state, message);
            _snapshot = Snapshot(state);
            await NotifyAsync(before, state);
        }
    }

    SafeDispose(state.SessionOrNull);   // shutdown: the loop owns final cleanup
}
```

### The reducer

```csharp
private async Task<BroadcastState> ApplyAsync(BroadcastState state, BroadcastMessage message)
{
    // Drop events addressed to a session we're no longer running.
    if (IsStaleEvent(state, message))
    {
        LogStaleEvent(message.GetType().Name);
        return state;
    }

    switch (state, message)
    {
        // ---- Idle ----
        case (BroadcastState.Idle, BroadcastMessage.Start start):
        {
            var channel = await _tunerClient.GetChannelAsync(start.GuideNumber);
            if (channel is null)
            {
                start.Reply.SetException(new InvalidOperationException(
                    $"Channel '{start.GuideNumber}' not found"));
                return state;
            }

            // CreateBroadcast + StartAsync are sub-second (DB read, process spawn),
            // so awaiting them here is fine for a single broadcast. If
            // enhancement-multi-broadcast.md lands, split this into Start ->
            // StartCompleted so a slow start can't stall other broadcasts.
            var session = await _sessionFactory.CreateBroadcast(channel);
            await session.StartAsync();

            WatchSession(session);
            start.Reply.SetResult(session.BroadcastInfo);   // matches today's timing
            return new BroadcastState.Starting(session);
        }

        case (BroadcastState.Idle, BroadcastMessage.Stop s):
            s.Reply.SetException(new InvalidOperationException("No active broadcast"));
            return state;

        case (BroadcastState.Idle, BroadcastMessage.Restart r):
            r.Reply.SetException(new InvalidOperationException("No active broadcast"));
            return state;

        // ---- Starting ----
        case (BroadcastState.Starting st, BroadcastMessage.StreamReady):
            return new BroadcastState.Ready(st.Session);

        case (BroadcastState.Starting st, BroadcastMessage.TranscodeEnded):
            SafeDispose(st.Session);
            return new BroadcastState.Idle();

        case (BroadcastState.Starting st, BroadcastMessage.Stop s):
            await st.Session.StopAsync();
            s.Reply.SetResult();
            return new BroadcastState.Stopping(st.Session);

        // ---- Ready ----
        case (BroadcastState.Ready rd, BroadcastMessage.Restart r):
            await rd.Session.RestartTranscodeAsync();
            Post(new BroadcastMessage.RestartCompleted(rd.Session.BroadcastInfo.SessionId));
            r.Reply.SetResult();
            return new BroadcastState.Restarting(rd.Session);

        case (BroadcastState.Ready rd, BroadcastMessage.Stop s):
            await rd.Session.StopAsync();
            s.Reply.SetResult();
            return new BroadcastState.Stopping(rd.Session);

        case (BroadcastState.Ready rd, BroadcastMessage.MaxDurationReached):
            await rd.Session.StopAsync();
            return new BroadcastState.Stopping(rd.Session);

        case (BroadcastState.Ready rd, BroadcastMessage.TranscodeEnded):
            SafeDispose(rd.Session);
            return new BroadcastState.Idle();

        // ---- Restarting ----
        case (BroadcastState.Restarting rs, BroadcastMessage.RestartCompleted):
            return new BroadcastState.Ready(rs.Session);

        case (BroadcastState.Restarting rs, BroadcastMessage.TranscodeEnded):
            SafeDispose(rs.Session);
            return new BroadcastState.Idle();

        case (BroadcastState.Restarting rs, BroadcastMessage.Stop s):
            await rs.Session.StopAsync();
            s.Reply.SetResult();
            return new BroadcastState.Stopping(rs.Session);

        // ---- Stopping ----
        case (BroadcastState.Stopping sp, BroadcastMessage.TranscodeEnded):
            SafeDispose(sp.Session);
            return new BroadcastState.Idle();

        // ---- Rejections ----
        case (_, BroadcastMessage.Start start):
            start.Reply.SetException(new InvalidOperationException(
                "Cannot start broadcast when another is already active!"));
            return state;

        default:
            LogIgnoredMessage(state.GetType().Name, message.GetType().Name);
            return state;
    }
}
```

### Session signals become messages

```csharp
private void WatchSession(BroadcastSession session)
{
    var id = session.BroadcastInfo.SessionId;

    // Fire-and-forget is fine HERE because these only post a message. The bug
    // today is fire-and-forget that *owns cleanup* — if Dispose() throws in
    // NotifyWhenStopped, _currentSession is never cleared and no further
    // broadcast can start.
    _ = Watch(session.HlsLiveStream.Ready, () => new BroadcastMessage.StreamReady(id));
    _ = Watch(session.Finished, () => new BroadcastMessage.TranscodeEnded(id, Expected: true));

    _maxDuration = new CancellationTokenSource(_hlsConfig.MaxDuration);
    _maxDuration.Token.Register(() => Post(new BroadcastMessage.MaxDurationReached(id)));

    async Task Watch(Task signal, Func<BroadcastMessage> toMessage)
    {
        try { await signal; }
        catch (Exception ex) { LogSignalFaulted(ex, id); }
        Post(toMessage());   // post regardless: a faulted Ready still ends the broadcast
    }
}

private void SafeDispose(BroadcastSession? session)
{
    if (session is null) return;
    try { session.Dispose(); }
    catch (Exception ex) { LogDisposeFailed(ex); }   // never propagates
}
```

### Decisions embedded above

These are the load-bearing parts; the rest is illustration.

- **Events carry `SessionId`.** Without it, a `StreamReady` from a session torn
  down mid-start can transition the session that replaced it. Classic actor bug,
  cheap to prevent.
- **`SafeDispose` never throws**, and the loop catches around every transition.
  Together these are the fix for the wedge: no disposal failure can leave the
  manager stuck in a non-`Idle` state.
- **`RunContinuationsAsynchronously` on every `TaskCompletionSource`.** Without
  it, `SetResult` runs the awaiting hub method's continuation inline on the loop
  thread, so a slow client stalls the mailbox.
- **Fire-and-forget is acceptable when the continuation only posts a message.**
  The distinction from today's bug is ownership, not the pattern itself.
- **`Stop` replies before `TranscodeEnded` arrives** — the caller learns the stop
  was accepted and initiated, with teardown one message later. If the control
  panel needs "fully stopped", hold the reply in the `Stopping` state instead.

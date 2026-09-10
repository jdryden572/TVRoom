# Enhancement — more than one broadcast at a time

**Status:** Aspirational — not planned, no current need · **Raised:** 2026-09-05

`BroadcastManager` allows exactly one active broadcast. This records what lifting
that would take, so the decision is available when it is wanted rather than
re-derived.

**One part of this cannot wait.** See [Decide now, build
later](#decide-now-build-later) — a schema choice made during other work is cheap
now and expensive to retrofit.

---

## Why it might be wanted

Two quite different cases, with very different cost profiles:

- **Concurrent on one agent** — two people in a household watching different
  channels. Bounded by that agent's tuner count *and* its uplink, since each
  broadcast is another contribution stream leaving the same residential
  connection.
- **Concurrent across agents** — households A and B each watching their own local
  channels. Separate tuners, separate uplinks, and the server merely fans out. The
  marginal cost is close to nothing.

**Multi-agent effectively implies at least the second case.** It would be strange
to serve several households from one server and have household A's broadcast block
household B's. So if
[architecture-server-agent-split.md](architecture-server-agent-split.md) ever
grows past a single agent, cross-agent concurrency stops being aspirational.

## What already supports it

More than you would expect. The external shapes anticipated this; the internals
did not.

| Already multi-capable | Note |
| --- | --- |
| `/streams/{sessionId}/...` | Keyed by session id already — only the *lookup* assumes one, comparing against `CurrentSession` |
| `/broadcast/current` | Already returns an array, always with 0 or 1 element |
| `TranscodeSessionManager` | Already a `ConcurrentDictionary` keyed by transcode id |
| `ScopedBufferPool`, `MergedHlsLiveStream`, `HlsStreamState` | Already per-broadcast instances |
| Signed URL tokens | Already scoped per session, see [enhancement-hls-signed-url-auth.md](enhancement-hls-signed-url-auth.md) |

---

## The state machine gets simpler

The design in
[enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md) is a
machine for one slot:

```
Idle | Starting(session) | Ready(session) | Restarting(session) | Stopping(session)
```

For several, the manager's state becomes a dictionary and every message carries a
broadcast id:

```csharp
IReadOnlyDictionary<BroadcastId, BroadcastState> _broadcasts;
// per-broadcast: Starting | Ready | Restarting | Stopping
```

**`Idle` disappears**, which is a real simplification — it was the only state with
no session, and the awkward one for the discriminated union. A broadcast that does
not exist is simply absent from the dictionary, so every remaining state carries a
session.

One mailbox still suffices at this scale: a handful of agents at 2–4 tuners each
is perhaps a dozen concurrent broadcasts, with rare commands and events at
two-second cadence.

But it makes one discipline **mandatory that is currently only advisable**:
keeping long `await`s out of the loop. With one broadcast you can block the loop
while ffmpeg spawns, because nobody else is waiting. With several, a slow start on
broadcast A stalls a stop on broadcast B. The `Starting`-state-plus-completion-message
pattern stops being a nicety.

## The genuinely new concept: admission

This exists in no form today. `Start` currently fails for one reason — another
broadcast is active. With several it fails for a **resource** reason: which agent,
and does it have a free tuner?

Tuner count is fixed per HDHomeRun, so concurrency per agent is physically
bounded. `TunerStatus` already carries `Resource` (which tuner) and `TargetIP`
(who is using it), so occupancy is observable — but **track your own allocations
as the source of truth** and treat `status.json` as advisory, since another
application on the network can take a tuner without telling you.

The rejection reason belongs in the transition log — *"rejected, no free tuner on
agent X"* is exactly what an admin needs to see.

## Where the mechanical work is

The hub and its client interface, which are pervasively single-broadcast:

```csharp
// today                        // needed
StartBroadcast(guideNumber)     StartBroadcast(agentId, guideNumber)
StopBroadcast()                 StopBroadcast(broadcastId)
RestartTranscode()              RestartTranscode(broadcastId)
GetCurrentSession()             GetCurrentSessions()
GetDebugOutput(ct)              GetDebugOutput(broadcastId, ct)
BroadcastStopped()              BroadcastStopped(broadcastId)   // takes no argument today
```

Plus the control panel moving from "the broadcast" to a list, and `IAgentProxy`
gaining a transcode id on every method — with `LocalAgent` and `RemoteAgent`
holding a dictionary and `ReportState` reporting a list for reconciliation.

## Two things this promotes to required

- **Broadcast history identity.** `EndCurrentBroadcast` closes "the latest row
  where `EndedAt` is null"; with concurrent broadcasts that closes an arbitrary
  one. The fix already recommended in
  [enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md) —
  return the record id, close by id — becomes mandatory rather than tidy.
- **SRT port pool.** One listener per concurrent broadcast rather than one port.

## A capacity caveat

Multi-broadcast partially erodes the motivation for the agent split. The point of
that work is that one copy leaves the residential network regardless of audience —
but that is *per broadcast*. Two concurrent broadcasts on one agent is 2×
contribution bitrate upstream. Still far better than scaling with viewers, but the
practical limit per agent may turn out to be **uplink rather than tuner count**: at
4–8 Mbps each, three concurrent broadcasts is a lot to ask of residential upload.

This is why the two cases at the top matter. Cross-agent concurrency does not have
this problem at all.

---

## Decide now, build later

Everything above can wait. One thing cannot, because it rides on a migration that
is already planned.

Item 0 of the decomposition in
[architecture-server-agent-split.md](architecture-server-agent-split.md) migrates
`BroadcastSessionRecord` to stop persisting the LAN-only tuner URL. Multi-broadcast
would later need that same table to carry an **agent id**, and multi-agent needs it
regardless.

**If multi-agent is even plausible, add a nullable `AgentId` column during that
migration even though nothing reads it yet.** An unused nullable column is close to
free; a second migration on a table with live history is not.

The other identity change — closing history rows by id rather than by heuristic —
is already committed as part of the lifecycle task, so nothing extra is needed
there.

## Open questions

- Is `SessionId` (the existing 32-character random string used in stream URLs)
  also the broadcast id, or does a separate id make sense? Reusing it is probably
  fine and avoids a second identifier.
- Should concurrency be capped by policy as well as by tuners — for instance a
  per-agent limit tuned to that site's upload bandwidth rather than its tuner
  count?
- Does the control panel show all broadcasts to every admin, or scope them by
  agent? Relevant only once multi-agent exists.

# Enhancement — use ffmpeg `-progress` instead of scraping stderr

**Status:** Proposed, not started · **Raised:** 2026-09-04

Replace stderr scraping with ffmpeg's structured progress output, so transcode
telemetry stops depending on a human-readable format that is not part of any
stability contract.

Independent of
[enhancement-broadcast-lifecycle.md](enhancement-broadcast-lifecycle.md); either
can ship first.

---

## Problem

`TranscodeStats.TryParse`
([TranscodeStats.cs](../TVRoom/Transcode/TranscodeStats.cs)) picks apart ffmpeg's
human-readable status line by searching for substrings:

```csharp
var fpsStart = line.IndexOf("fps=");
var qualityStart = line.IndexOf("q=");
var sizeStart = line.IndexOf("size=");
var speedStart = line.IndexOf("speed=");
```

It then slices between the markers, and separately hunts for `dup=` and `drop=`.
Three problems with this:

1. **The format is not a contract.** ffmpeg's status line is presentation output.
   Field order, spacing, and which fields appear at all vary by version and by
   codec configuration. The Dockerfile pins `lscr.io/linuxserver/ffmpeg:7.0.2`,
   which contains the risk but also means any upgrade is a potential silent
   telemetry break — silent because `TryParse` simply returns `false` and the
   stats stream goes quiet rather than failing loudly.
2. **`q=` is looked up by an unanchored substring.** `IndexOf("q=")` matches the
   first `q=` anywhere in the line, and slices from there to `size=`. Any earlier
   token ending in `q` (for instance `freq=`) would silently shift the parse.
3. **One string stream serves two masters.** The same stderr observable feeds
   both the debug log and the stats parser, so the log format and the telemetry
   format are coupled to each other.

---

## Design

Use `-progress <url>`, which emits a structured block of `key=value` lines at a
fixed interval, terminated by `progress=continue` (or `progress=end` on the final
block):

```
frame=1234
fps=29.97
stream_0_0_q=24.0
total_size=1048576
out_time_us=41000000
dup_frames=0
drop_frames=2
speed=1.01x
progress=continue
```

These are documented field names rather than a rendered line, and the block
boundary (`progress=`) makes each sample explicitly complete — which removes the
current implicit assumption that one stderr line equals one sample.

### Transport

Two options; the first fits the existing architecture better.

**a. A second HTTP ingest endpoint.** You already run ffmpeg writing to
`http://127.0.0.1:{port}/transcode/{id}/...`
([HlsConfiguration](../TVRoom/Configuration/HlsConfiguration.cs) builds
`HlsIngestBaseAddress`), and `TranscodeSessionManager` already routes by
`transcodeId`. Add `-progress {base}/{id}/progress` and a matching endpoint that
parses blocks into `TranscodeStats`. Reuses the routing, the session lookup, and
the loopback restriction described in issue 4 of
[known-issues.md](known-issues.md).

**b. A pipe.** `-progress pipe:1` writes to stdout, which is currently unused —
`FFmpegProcess` redirects stdout but only ever reads stderr. Fewer moving parts,
but it means one more stream to pump and it does not reuse the ingest path.

### What this changes downstream

- `TranscodeSession.Stats` is built from the progress stream instead of
  `FFmpegOutput` ([TranscodeSession.cs:31](../TVRoom/Transcode/TranscodeSession.cs#L31)).
- Stderr becomes log-only, feeding `WriteTranscodeLogsToFile` and the debug
  stream and nothing else.
- `ConditionalMap` ([ObservableExtensions.cs:7](../TVRoom/Helpers/ObservableExtensions.cs#L7))
  loses its only caller and can be deleted. Worth noting it was a poor fit
  anyway: it subscribes eagerly and discards the `IDisposable`, so it can never
  be unsubscribed. It is `Select` + `Where` in disguise, and plain Rx operators
  are lazy and disposable.

---

## Implementation steps

1. Add `-progress` to the argument string in
   [TranscodeSession.cs:28](../TVRoom/Transcode/TranscodeSession.cs#L28), alongside
   the existing `hlsSettings`. Consider `-stats_period` to set the interval
   explicitly rather than relying on the default.
2. Add a block parser: accumulate `key=value` lines until `progress=`, then emit
   one `TranscodeStats`. This is a cleaner parser than the current one and is
   straightforward to unit test — note the existing tests do not currently
   compile, see issue 1 in [known-issues.md](known-issues.md).
3. Route the progress stream into `TranscodeSession.Stats`.
4. Decide whether to keep the current `TranscodeStats` shape. `q` is per-stream
   in progress output (`stream_0_0_q`), and `out_time_us` is available and more
   useful than the current `size=` which is parsed but discarded.
5. Remove the stats path from the stderr observable, and delete `ConditionalMap`.

---

## Also in scope

Same pipeline, worth doing while it is open:

- **The debug output replay is unbounded in size.** `Replay(DebugOutputRetention)`
  in [BroadcastSession.cs](../TVRoom/Broadcast/BroadcastSession.cs#L41) buffers 60
  seconds of stderr with no cap on volume; ffmpeg can be chatty on a struggling
  stream. Consider a bounded replay.
- **The `Connect()` disposable is discarded** rather than added to `_cleanup`, so
  the connection is torn down only by source completion, not by disposal.

## Not in scope

`TunerStatusProvider` telemetry
([TunerStatusProvider.cs:17-19](../TVRoom/Tuner/TunerStatusProvider.cs#L17-L19))
is a separate concern — it polls the tuner over HTTP rather than reading ffmpeg,
and its `Replay(61).RefCount()` lifetime question is unrelated to this change.

## Risks

- `-progress` interval and the current stderr cadence may differ, changing how
  often the control panel updates. The hub already applies `.Sample(1s)`
  ([ControlPanelHub.cs:84](../TVRoom/Broadcast/ControlPanelHub.cs#L84)), so verify
  the two do not compound into a visibly slower refresh.
- Option (a) adds an endpoint that must be covered by whatever loopback
  restriction lands for the other ingest routes; do not leave it anonymous and
  publicly bound.
- Verify against the pinned ffmpeg 7.0.2 build in the Dockerfile before relying
  on any specific field name.

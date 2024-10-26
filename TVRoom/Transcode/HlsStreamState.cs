using System.Buffers;
using Utf8StringInterpolation;

namespace TVRoom.Transcode
{
    public sealed record HlsStreamState(
        string StreamInfo,
        int HlsVersion,
        double TargetDuration,
        int MediaSequence,
        FixedQueue<HlsPlaylistEntry> LiveEntries,
        FixedQueue<HlsPlaylistEntry> PreviousSegments)
    {
        public static HlsStreamState GetNextState(HlsStreamState? previous, HlsSegmentInfo segmentInfo, int hlsListSize)
        {
            if (previous is null)
            {
                var segment = new HlsSegmentPlaylistEntry(0, segmentInfo.Duration, segmentInfo.Segment);
                return new HlsStreamState(
                    segmentInfo.StreamInfo,
                    segmentInfo.HlsVersion,
                    segmentInfo.TargetDuration,
                    MediaSequence: 0,
                    new FixedQueue<HlsPlaylistEntry>(hlsListSize).Push(segment, out _),
                    new FixedQueue<HlsPlaylistEntry>(hlsListSize));
            }

            var lastIndex = previous.Segments.LastOrDefault()?.Index ?? -1;
            var newSegment = new HlsSegmentPlaylistEntry(lastIndex + 1, segmentInfo.Duration, segmentInfo.Segment);

            var entries = previous.LiveEntries.Push(newSegment, out var moveToPrevious);
            var previousEntries = previous.PreviousSegments;
            if (moveToPrevious is not null)
            {
                previousEntries = previousEntries.Push(moveToPrevious, out var toBeDisposed);
                toBeDisposed?.Dispose();
            }

            var newStream = previous with
            {
                MediaSequence = entries.OfType<HlsSegmentPlaylistEntry>().First().Index,
                LiveEntries = entries,
                PreviousSegments = previousEntries,
            };

            return newStream;
        }

        public static HlsStreamState GetNextStateForDiscontinuity(HlsStreamState previous, int hlsListSize)
        {
            var newEntry = new HlsDiscontinuityPlaylistEntry();

            var entries = previous.LiveEntries.Push(newEntry, out var moveToPrevious);
            var previousEntries = previous.PreviousSegments;
            if (moveToPrevious is not null)
            {
                previousEntries = previousEntries.Push(moveToPrevious, out var toBeDisposed);
                toBeDisposed?.Dispose();
            }

            var newStream = previous with
            {
                MediaSequence = entries.OfType<HlsSegmentPlaylistEntry>().First().Index,
                LiveEntries = entries,
                PreviousSegments = previousEntries,
            };

            return newStream;
        }

        private IEnumerable<HlsSegmentPlaylistEntry> Segments => LiveEntries.OfType<HlsSegmentPlaylistEntry>();

        public void WriteMasterPlaylist(IBufferWriter<byte> writer)
        {
            using (new InvariantContext())
            {
                Utf8String.Format(writer,
                    $"""
                    #EXTM3U
                    #EXT-X-VERSION:{HlsVersion}
                    #EXT-X-STREAM-INF:{StreamInfo}
                    live.m3u8
                    """);
            }
        }

        public void WriteStreamPlaylist(IBufferWriter<byte> writer)
        {
            using (new InvariantContext())
            {
                Utf8String.Format(writer,
                    $"""
                    #EXTM3U
                    #EXT-X-VERSION:{HlsVersion}
                    #EXT-X-TARGETDURATION:{TargetDuration}
                    #EXT-X-MEDIA-SEQUENCE:{MediaSequence}
                    """);
            }

            foreach (var entry in LiveEntries)
            {
                entry.WriteTo(writer);
            }
        }

        public IResult GetSegmentResult(int index)
        {
            foreach (var entry in LiveEntries)
            {
                if (entry is HlsSegmentPlaylistEntry segmentEntry && segmentEntry.Index == index)
                {
                    return segmentEntry.Segment.GetResult();
                }
            }

            foreach (var entry in PreviousSegments)
            {
                if (entry is HlsSegmentPlaylistEntry segmentEntry && segmentEntry.Index == index)
                {
                    return segmentEntry.Segment.GetResult();
                }
            }

            return Results.NotFound();
        }
    }

    public abstract record HlsPlaylistEntry : IDisposable
    {
        public abstract void WriteTo(IBufferWriter<byte> writer);

        public virtual void Dispose() { }
    }

    public sealed record HlsSegmentPlaylistEntry(int Index, double Duration, HlsSegment Segment) : HlsPlaylistEntry
    {
        public override void WriteTo(IBufferWriter<byte> writer)
        {
            using (new InvariantContext())
            {
                Utf8String.Format(writer, $"\r\n#EXTINF:{Duration:N6},\r\nlive{Index}.ts");
            }
        }

        public override void Dispose() => Segment.Dispose();
    }

    public sealed record HlsDiscontinuityPlaylistEntry : HlsPlaylistEntry
    {
        public override void WriteTo(IBufferWriter<byte> writer) =>
            writer.Write("\r\n#EXT-X-DISCONTINUITY"u8);
    }
}

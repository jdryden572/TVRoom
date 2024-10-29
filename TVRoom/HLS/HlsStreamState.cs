using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using Utf8StringInterpolation;

namespace TVRoom.HLS
{
    public abstract record HlsStreamState
    {
        private readonly int _hlsListSize;

        public HlsStreamState(int hlsListSize)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(hlsListSize, 2);
            _hlsListSize = hlsListSize;
        }

        public abstract HlsStreamState WithNewSegment(HlsSegmentInfo segmentInfo);
        public abstract HlsStreamState WithNewDiscontinuity();

        // Intentionally not using IDisposable pattern here, so
        // it doesn't imply the objects should always be disposed.
        public virtual void DisposeAllSegments()
        {
        }

        public abstract IResult GetMasterPlaylist();
        public abstract IResult GetPlaylist();
        public abstract IResult GetSegment(int index);
    }

    public sealed record HlsStreamNotReady(int HlsListSize) : HlsStreamState(HlsListSize)
    {
        public override HlsStreamState WithNewDiscontinuity()
        {
            return this;
        }

        public override HlsStreamState WithNewSegment(HlsSegmentInfo segmentInfo)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(HlsListSize, 2);

            var segment = new HlsSegmentEntry(0, segmentInfo.Duration, segmentInfo.Payload);
            return new HlsStreamWithSegments(
                HlsListSize,
                segmentInfo.StreamInfo,
                segmentInfo.HlsVersion,
                segmentInfo.TargetDuration,
                new HlsSegmentList(HlsListSize).Push(segment, out _),
                new HlsSegmentList(HlsListSize));
        }

        public override IResult GetMasterPlaylist() => Results.NotFound();
        public override IResult GetPlaylist() => Results.NotFound();
        public override IResult GetSegment(int index) => Results.NotFound();
    }

    public sealed record HlsStreamWithSegments(
        int HlsListSize,
        string StreamInfo,
        int HlsVersion,
        double TargetDuration,
        HlsSegmentList LiveSegments,
        HlsSegmentList PreviousSegments) : HlsStreamState(HlsListSize)
    {
        public override HlsStreamState WithNewDiscontinuity()
        {
            var lastSegment = LiveSegments[^1];
            return this with
            {
                LiveSegments = LiveSegments.Replace(^1, lastSegment.WithDiscontinuity()),
            };
        }

        public override HlsStreamState WithNewSegment(HlsSegmentInfo segmentInfo)
        {
            var latestIndex = LiveSegments[^1].Index;
            var newSegmentEntry = new HlsSegmentEntry(latestIndex + 1, segmentInfo.Duration, segmentInfo.Payload);

            var newLiveSegments = LiveSegments.Push(newSegmentEntry, out var moveToPrevious);
            var newPreviousSegments = PreviousSegments;
            if (moveToPrevious is not null)
            {
                newPreviousSegments = PreviousSegments.Push(moveToPrevious, out var toBeDisposed);
                toBeDisposed?.Payload.Dispose();
            }

            var newStreamState = this with
            {
                LiveSegments = newLiveSegments,
                PreviousSegments = newPreviousSegments,
            };

            return newStreamState;
        }

        public int MediaSequence => LiveSegments[0].Index;

        public override void DisposeAllSegments()
        {
            foreach (var segment in LiveSegments)
            {
                segment.Payload.Dispose();
            }

            foreach (var segment in PreviousSegments)
            {
                segment.Payload.Dispose();
            }
        }

        public override IResult GetMasterPlaylist() => new MasterPlaylistResult(this);

        public override IResult GetPlaylist() => new StreamPlaylistResult(this);

        public override IResult GetSegment(int index)
        {
            var segment = LiveSegments.FirstOrDefault(i => i.Index == index) ?? 
                PreviousSegments.FirstOrDefault(i => i.Index == index);

            return segment is not null ? 
                new SegmentResult(segment.Payload.Rent()) : 
                Results.NotFound();
        }

        private sealed record MasterPlaylistResult(HlsStreamWithSegments state) : IResult
        {
            public async Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.Headers.ContentType = "audio/mpegurl";

                using (new CultureContext(CultureInfo.InvariantCulture))
                {
                    Utf8String.Format(httpContext.Response.BodyWriter,
                        $"""
                        #EXTM3U
                        #EXT-X-VERSION:{state.HlsVersion}
                        #EXT-X-STREAM-INF:{state.StreamInfo}
                        live.m3u8
                        """);
                }

                await httpContext.Response.BodyWriter.FlushAsync();
            }
        }

        private sealed record StreamPlaylistResult(HlsStreamWithSegments state) : IResult
        {
            public async Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.Headers.ContentType = "audio/mpegurl";

                using (new CultureContext(CultureInfo.InvariantCulture))
                {
                    var writer = httpContext.Response.BodyWriter;
                    Utf8String.Format(writer,
                         $"""
                        #EXTM3U
                        #EXT-X-VERSION:{state.HlsVersion}
                        #EXT-X-TARGETDURATION:{state.TargetDuration}
                        #EXT-X-MEDIA-SEQUENCE:{state.MediaSequence}
                        """);

                    foreach (var segment in state.LiveSegments)
                    {
                        segment.WriteTo(writer);
                    }
                }

                await httpContext.Response.BodyWriter.FlushAsync();
            }
        }

        private sealed record SegmentResult(IBufferLease lease) : IResult
        {
            public async Task ExecuteAsync(HttpContext httpContext)
            {
                using (lease)
                {
                    httpContext.Response.Headers.ContentType = "application/octet-stream";
                    await httpContext.Response.BodyWriter.WriteAsync(lease.GetMemory());
                }
            }
        }
    }

    public record HlsSegmentEntry(int Index, double Duration, SharedBuffer Payload)
    {
        public HlsSegmentFollowedByDiscontinuity WithDiscontinuity() => 
            new HlsSegmentFollowedByDiscontinuity(Index, Duration, Payload);

        public virtual void WriteTo(IBufferWriter<byte> writer)
        {
            Utf8String.Format(writer, $"\r\n#EXTINF:{Duration:N6},\r\nlive{Index}.ts");
        }
    }

    public sealed record HlsSegmentFollowedByDiscontinuity(int Index, double Duration, SharedBuffer Payload) 
        : HlsSegmentEntry(Index, Duration, Payload)
    {
        public override void WriteTo(IBufferWriter<byte> writer)
        {
            base.WriteTo(writer);
            writer.Write("\r\n#EXT-X-DISCONTINUITY"u8);
        }
    }
}

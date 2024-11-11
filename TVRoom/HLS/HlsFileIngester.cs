using System.Reactive.Linq;
using System.Threading.Channels;

namespace TVRoom.HLS
{
    public sealed class HlsFileIngester : IDisposable
    {
        private readonly Channel<IngestHlsFile> _channel = Channel.CreateBounded<IngestHlsFile>(new BoundedChannelOptions(10));

        public HlsFileIngester()
        {
            // Use Publish() to ensure the observable factory is started immediately.
            var segmentInfos = Observable.Create<HlsSegmentInfo>(ProcessIngestedFiles).Publish();
            segmentInfos.Connect();
            StreamSegments = segmentInfos;
        }

        public IObservable<HlsSegmentInfo> StreamSegments { get; }

        public async Task IngestStreamFileAsync(IngestHlsFile ingestFile) => await _channel.Writer.WriteAsync(ingestFile);

        public void Dispose()
        {
            _channel.Writer.TryComplete();
        }

        private async Task ProcessIngestedFiles(IObserver<HlsSegmentInfo> obs)
        {
            ParsedMasterPlaylist? masterPlaylist = null;
            ParsedStreamPlaylist? streamPlaylist = null;
            Queue<IngestStreamSegment> segmentQueue = new();

            await foreach (var file in _channel.Reader.ReadAllAsync())
            {
                file.Payload.UpdatedUseLocation(BufferUseLocation.Ingest);

                if (file is IngestMasterPlaylist master && master.TryParse(out var parsedMasterPlaylist))
                {
                    masterPlaylist = parsedMasterPlaylist;
                    file.Payload.Dispose();
                }
                else if (file is IngestStreamPlaylist stream && stream.TryParse(out var parsedStreamPlaylist))
                {
                    streamPlaylist = parsedStreamPlaylist;
                    file.Payload.Dispose();
                }
                else if (file is IngestStreamSegment segment)
                {
                    // If there is still a segment in the queue when we get a new one, the previous one
                    // was unmatched by a playlist entry. We will never publish it, so we must dispose it here.
                    if (segmentQueue.TryDequeue(out var unusedSegment))
                    {
                        unusedSegment.Payload.Dispose();
                    }

                    segmentQueue.Enqueue(segment);
                }

                if (masterPlaylist is null || streamPlaylist is null || segmentQueue.Count == 0)
                {
                    // Need to receive all three kinds of files before we can yield anything
                    continue;
                }

                var latestSegmentRef = streamPlaylist.SegmentReferences[^1];
                if (latestSegmentRef.FileName == segmentQueue.Peek().FileName)
                {
                    var segment = segmentQueue.Dequeue();
                    var info = new HlsSegmentInfo(
                        masterPlaylist.StreamInfo,
                        streamPlaylist.HlsVersion,
                        streamPlaylist.TargetDuration,
                        latestSegmentRef.Duration,
                        segment.Payload);

                    segment.Payload.UpdatedUseLocation(BufferUseLocation.CreateSegmentInfo);

                    obs.OnNext(info);
                }
            }

            // Dispose any segments we haven't processed
            while (segmentQueue.TryDequeue(out var segment))
            {
                segment.Payload.Dispose();
            }
        }
    }
}

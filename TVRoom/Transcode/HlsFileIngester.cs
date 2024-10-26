using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Channels;

namespace TVRoom.Transcode
{

    public sealed class HlsFileIngester : IDisposable
    {
        private readonly Channel<IngestHlsFile> _channel = Channel.CreateBounded<IngestHlsFile>(new BoundedChannelOptions(50));
        private readonly Queue<HlsSegment> _segmentQueue = new();
        private IngestMasterPlaylist? _masterPlaylist;
        private IngestStreamPlaylist? _latestStreamPlaylist;

        private readonly Subject<HlsSegmentInfo> _streamSegmentSubject = new();
        public IObservable<HlsSegmentInfo> StreamSegments => _streamSegmentSubject.AsObservable();

        public HlsFileIngester()
        {
            _ = Task.Run(ProcessFiles);
        }

        public async Task IngestStreamFileAsync(IngestHlsFile ingestFile) => await _channel.Writer.WriteAsync(ingestFile);

        private async Task ProcessFiles()
        {
            // Using channel here to ensure we process files one at a time (even if uploaded concurrently)
            await foreach (var file in _channel.Reader.ReadAllAsync())
            {
                switch (file.FileType)
                {
                    case IngestFileType.MasterPlaylist:
                        if (IngestMasterPlaylist.TryParse(file.Payload.Span, out var parsedMaster))
                        {
                            _masterPlaylist = parsedMaster;
                        }

                        // Return payload buffer to pool
                        file.Payload.Dispose();
                        break;

                    case IngestFileType.Playlist:
                        if (IngestStreamPlaylist.TryParse(file.Payload.Span, out var parsedStreamPlaylist))
                        {
                            _latestStreamPlaylist = parsedStreamPlaylist;
                        }

                        // Return payload buffer to pool
                        file.Payload.Dispose();
                        break;

                    case IngestFileType.Segment:
                        // If there is still a segment in the queue when we get a new one, the previous one
                        // was unmatched by a playlist entry. We will never publish it, so we must dispose it here.
                        if (_segmentQueue.TryDequeue(out var unusedSegment))
                        {
                            unusedSegment.Dispose();
                        }

                        _segmentQueue.Enqueue(new HlsSegment(file.FileName, file.Payload));
                        break;

                    default:
                        // Return payload buffer to pool
                        file.Payload.Dispose();
                        break;
                }

                if (_masterPlaylist is null || _latestStreamPlaylist is null || _segmentQueue.Count == 0 || _latestStreamPlaylist.SegmentReferences.Count == 0)
                {
                    continue;
                }

                var latestSegmentRef = _latestStreamPlaylist.SegmentReferences.LastOrDefault();

                var nextSegment = _segmentQueue.Peek();
                if (nextSegment.FileName == latestSegmentRef.FileName)
                {
                    var segment = _segmentQueue.Dequeue();
                    var hlsStreamSegment = new HlsSegmentInfo(
                        _masterPlaylist.StreamInfo,
                        _latestStreamPlaylist.HlsVersion,
                        _latestStreamPlaylist.TargetDuration,
                        latestSegmentRef.Duration,
                        segment);

                    _streamSegmentSubject.OnNext(hlsStreamSegment);
                }
            }

            // Dispose any segments we haven't processed
            while (_segmentQueue.TryDequeue(out var segment))
            {
                segment.Dispose();
            }

            _streamSegmentSubject.OnCompleted();
        }

        public void Dispose()
        {
            _channel.Writer.Complete();
        }
    }
}

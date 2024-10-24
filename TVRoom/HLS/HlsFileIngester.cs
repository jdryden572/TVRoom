using CommunityToolkit.HighPerformance.Buffers;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Channels;

namespace TVRoom.HLS
{
    public sealed record HlsIngestFile(string FileName, HlsFileType FileType, MemoryOwner<byte> Payload);

    public sealed class HlsFileIngester : IDisposable
    {
        private readonly Channel<HlsIngestFile> _channel = Channel.CreateBounded<HlsIngestFile>(new BoundedChannelOptions(50));
        private readonly Queue<HlsSegment> _segmentQueue = new();
        private HlsMasterPlaylist? _masterPlaylist;
        private HlsStreamPlaylist? _latestStreamPlaylist;

        private readonly Subject<HlsStreamSegment> _streamSegmentSubject = new();
        public IObservable<HlsStreamSegment> StreamSegments => _streamSegmentSubject.AsObservable();

        public HlsFileIngester()
        {
            _ = Task.Run(ProcessFiles);
        }

        public async Task IngestStreamFileAsync(HlsIngestFile ingestFile) => await _channel.Writer.WriteAsync(ingestFile);

        private async Task ProcessFiles()
        {
            // Using channel here to ensure we process files one at a time (even if uploaded concurrently)
            await foreach (var file in _channel.Reader.ReadAllAsync())
            {
                switch (file.FileType)
                {
                    case HlsFileType.MasterPlaylist:
                        if (HlsMasterPlaylist.TryParse(file.Payload.Span, out var parsedMaster))
                        {
                            _masterPlaylist = parsedMaster;
                        }

                        // Return payload buffer to pool
                        file.Payload.Dispose();
                        break;

                    case HlsFileType.Playlist:
                        if (HlsStreamPlaylist.TryParse(file.Payload.Span, out var parsedStreamPlaylist))
                        {
                            _latestStreamPlaylist = parsedStreamPlaylist;
                        }

                        // Return payload buffer to pool
                        file.Payload.Dispose();
                        break;

                    case HlsFileType.Segment:
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
                    var hlsStreamSegment = new HlsStreamSegment(
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

    public sealed record HlsStreamSegment(
        string StreamInfo,
        int HlsVersion,
        int TargetDuration,
        double Duration,
        HlsSegment Segment);

    public interface IHlsFile
    {
        IResult GetResult();
    }

    public sealed class HlsMasterPlaylist
    {
        public int HlsVersion { get; }
        public string StreamInfo { get; }

        private HlsMasterPlaylist(int hlsVersion, string streamInfo)
        {
            HlsVersion = hlsVersion;
            StreamInfo = streamInfo;
        }

        public static bool TryParse(ReadOnlySpan<byte> payload, [MaybeNullWhen(false)] out HlsMasterPlaylist parsed)
        {
            var version = int.MinValue;
            var streamInfo = string.Empty;

            var buffer = ArrayPool<char>.Shared.Rent(payload.Length * 2);

            try
            {
                var length = Encoding.UTF8.GetChars(payload, buffer);
                var text = buffer.AsSpan().Slice(0, length);
                foreach (var line in MemoryExtensions.EnumerateLines(text))
                {
                    const string versionPrefix = "#EXT-X-VERSION:";
                    if (line.StartsWith(versionPrefix) &&
                        int.TryParse(line.Slice(versionPrefix.Length), CultureInfo.InvariantCulture, out var parsedVersion))
                    {
                        version = parsedVersion;
                        continue;
                    }

                    const string streamInfoPrefix = "#EXT-X-STREAM-INF:";
                    if (line.StartsWith(streamInfoPrefix))
                    {
                        streamInfo = line.Slice(streamInfoPrefix.Length).ToString();
                    }
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            if (version > int.MinValue && !string.IsNullOrEmpty(streamInfo))
            {
                parsed = new HlsMasterPlaylist(version, streamInfo);
                return true;
            }

            parsed = null;
            return false;
        }
    }

    public record struct HlsSegmentReference(string FileName, double Duration);

    public sealed class HlsStreamPlaylist
    {
        public int HlsVersion { get; }
        public int TargetDuration { get; }
        public IReadOnlyList<HlsSegmentReference> SegmentReferences { get; }

        private HlsStreamPlaylist(int hlsVersion, int targetDuration, IReadOnlyList<HlsSegmentReference> segmentReferences)
        {
            HlsVersion = hlsVersion;
            TargetDuration = targetDuration;
            SegmentReferences = segmentReferences;
        }

        public static bool TryParse(ReadOnlySpan<byte> payload, [MaybeNullWhen(false)] out HlsStreamPlaylist parsed)
        {
            var version = int.MinValue;
            var targetDuration = int.MinValue;
            var segments = new List<HlsSegmentReference>();

            var buffer = ArrayPool<char>.Shared.Rent(payload.Length * 2);

            try
            {
                var length = Encoding.UTF8.GetChars(payload, buffer);
                var text = buffer.AsSpan().Slice(0, length);

                var currentSegmentDuration = double.MinValue;
                
                foreach (var line in MemoryExtensions.EnumerateLines(text))
                {
                    if (TryParseVersion(line, out var parsedVersion))
                    {
                        version = parsedVersion;
                        continue;
                    }

                    if (TryParseTargetDuration(line, out var parsedTargetDuration))
                    {
                        targetDuration = parsedTargetDuration;
                        continue;
                    }

                    if (currentSegmentDuration > double.MinValue && !line.StartsWith("#"))
                    {
                        // previous line was #EXTINF:
                        var segment = new HlsSegmentReference(line.ToString(), currentSegmentDuration);
                        segments.Add(segment);
                    }

                    if (TryParseSegmentDuration(line, out currentSegmentDuration))
                    {
                        continue;
                    }
                    else
                    {
                        currentSegmentDuration = double.MinValue;
                    }
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            if (version > int.MinValue && targetDuration > int.MinValue)
            {
                parsed = new HlsStreamPlaylist(version, targetDuration, segments.AsReadOnly());
                return true;
            }

            parsed = null;
            return false;
        }

        private static bool TryParseVersion(ReadOnlySpan<char> line, out int version)
        {
            const string prefix = "#EXT-X-VERSION:";
            if (line.StartsWith(prefix) && 
                int.TryParse(line.Slice(prefix.Length), CultureInfo.InvariantCulture, out version))
            {
                return true;
            }

            version = int.MinValue;
            return false;
        }

        private static bool TryParseTargetDuration(ReadOnlySpan<char> line, out int targetDuration)
        {
            const string prefix = "#EXT-X-TARGETDURATION:";
            if (line.StartsWith(prefix) &&
                int.TryParse(line.Slice(prefix.Length), CultureInfo.InvariantCulture, out targetDuration))
            {
                return true;
            }

            targetDuration = int.MinValue;
            return false;
        }

        private static bool TryParseSegmentDuration(ReadOnlySpan<char> line, out double duration)
        {
            const string prefix = "#EXTINF:";
            if (line.StartsWith(prefix) && line.EndsWith(","))
            {
                var slice = line.Slice(prefix.Length, line.Length - prefix.Length - 1);
                if (double.TryParse(slice, CultureInfo.InvariantCulture, out duration))
                {
                    return true;
                }
            }

            duration = double.MinValue;
            return false;
        }
    }

    public sealed class HlsSegment : IHlsFile, IDisposable
    {
        private readonly object _lock = new();

        // Synchronize access to these with the lock
        private MemoryOwner<byte>? _buffer;
        private int _refCount;
        private bool _disposed;

        public string FileName { get; }
        public int Length => _buffer?.Length ?? 0;

        public HlsSegment(string fileName, MemoryOwner<byte> buffer)
        {
            FileName = fileName;
            _buffer = buffer;
        }

        public IResult GetResult()
        {
            ReadOnlyMemory<byte> memory;
            lock (_lock)
            {
                if (_disposed || _buffer is null)
                {
                    throw new ObjectDisposedException(FileName);
                }

                _refCount++;
                memory = _buffer.Memory;
            }

            return new HlsSegmentResult(this, memory);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
            }

            ReturnBufferIfDisposedAndRefCountZero();
        }

#if DEBUG
        ~HlsSegment()
        {
            Debug.Fail($"HlsSegement is being finalized! '{FileName}'");
        }
#endif

        private void ReaderDisposed()
        {
            lock (_lock)
            {
                _refCount--;
            }

            ReturnBufferIfDisposedAndRefCountZero();
        }

        private void ReturnBufferIfDisposedAndRefCountZero()
        {
            MemoryOwner<byte>? bufferToReturn = null;
            lock (_lock)
            {
                if (_disposed && _refCount == 0)
                {
                    bufferToReturn = _buffer;
                    _buffer = null;
                }
            }

            if (bufferToReturn is not null)
            {
                GC.SuppressFinalize(this);
                bufferToReturn.Dispose();
            }
        }


        private sealed class HlsSegmentResult : IResult
        {
            private readonly HlsSegment _hlsSegment;
            private readonly ReadOnlyMemory<byte> _buffer;
            private bool _finished;

            public HlsSegmentResult(HlsSegment hlsSegment, ReadOnlyMemory<byte> buffer)
            {
                _hlsSegment = hlsSegment;
                _buffer = buffer;
            }

            public async Task ExecuteAsync(HttpContext httpContext)
            {
                if (_finished)
                {
                    throw new InvalidOperationException("Cannot execute HlsSegmentResult more than once!");
                }

                _finished = true;

                httpContext.Response.Headers.ContentType = "application/octet-stream";

                try
                {
                    await httpContext.Response.BodyWriter.WriteAsync(_buffer);
                }
                finally
                {
                    _hlsSegment.ReaderDisposed();
                }
            }
        }
    }
}

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Toolkit.HighPerformance.Buffers;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipelines;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TVRoom.Broadcast;

namespace TVRoom.HLS
{
    public sealed class HlsFileIngester : IDisposable
    {
        private readonly Subject<HlsMasterPlaylist> _masterPlaylists = new();
        private readonly Subject<HlsStreamPlaylist> _variantPlaylists = new();
        private readonly Subject<HlsSegment> _segments = new();

        private readonly Channel<IngestFile> _channel = Channel.CreateBounded<IngestFile>(new BoundedChannelOptions(50));

        public HlsFileIngester()
        {
            //_ = Task.Run()
        }

        public async Task IngestStreamFileAsync(string fileName, PipeReader fileContentsReader)
        {
            var fileType = fileName switch
            {
                "master.m3u8" => HlsFileType.MasterPlaylist,
                "live.m3u8" => HlsFileType.Playlist,
                _ => HlsFileType.Segment,
            };

            if (fileType == HlsFileType.Segment && Path.GetExtension(fileName) != ".ts")
            {
                throw new ArgumentException("Unrecognized file extension in HLS file!", nameof(fileName));
            }

            var fileContents = await fileContentsReader.PooledReadToEndAsync();
            await _channel.Writer.WriteAsync(new IngestFile(fileName, fileType, fileContents));
        }

        private async Task ListenForFiles()
        {
            await foreach (var file in _channel.Reader.ReadAllAsync())
            {
                //switch (file.FileType)
                //{
                //    case HlsFileType.MasterPlaylist:
                //        var contents = Encoding.UTF8.GetString(file.FileContents.Span);

                //}
            }
        }

        public void Dispose()
        {
            _masterPlaylists.OnCompleted();
            _variantPlaylists.OnCompleted();
            _segments.OnCompleted();
        }

        public IObservable<HlsMasterPlaylist> MasterPlaylists => _masterPlaylists.AsObservable();
        public IObservable<HlsStreamPlaylist> VariantPlaylists => _variantPlaylists.AsObservable();
        public IObservable<HlsSegment> Segments => _segments.AsObservable();

        private sealed record IngestFile(string FileName, HlsFileType FileType, MemoryOwner<byte> FileContents);
    }

    public interface IHlsFile
    {
        IResult GetResult();
    }

    public sealed class HlsMasterPlaylist : IHlsFile
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

        public IResult GetResult()
        {
            throw new NotImplementedException();
        }
    }

    public record struct HlsSegmentReference(string FileName, double Duration);

    public sealed class HlsStreamPlaylist : IHlsFile
    {
        public int HlsVersion { get; }
        public long TargetDuration { get; }
        public IReadOnlyList<HlsSegmentReference> SegmentReferences { get; }

        private HlsStreamPlaylist(int hlsVersion, long targetDuration, IReadOnlyList<HlsSegmentReference> segmentReferences)
        {
            HlsVersion = hlsVersion;
            TargetDuration = targetDuration;
            SegmentReferences = segmentReferences;
        }

        public static bool TryParse(ReadOnlySpan<byte> payload, [MaybeNullWhen(false)] out HlsStreamPlaylist parsed)
        {
            var version = int.MinValue;
            var targetDuration = long.MinValue;
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

            if (version > int.MinValue && targetDuration > long.MinValue)
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

        private static bool TryParseTargetDuration(ReadOnlySpan<char> line, out long targetDuration)
        {
            const string prefix = "#EXT-X-TARGETDURATION:";
            if (line.StartsWith(prefix) && 
                long.TryParse(line.Slice(prefix.Length), CultureInfo.InvariantCulture, out targetDuration))
            {
                return true;
            }

            targetDuration = long.MinValue;
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

        public IResult GetResult()
        {
            throw new NotImplementedException();
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
        public int Length { get; }

        public static async Task<HlsSegment> ReadAsync(string fileName, PipeReader fileContents)
        {
            var buffer = await fileContents.PooledReadToEndAsync();
            return new HlsSegment(fileName, buffer);
        }

        private HlsSegment(string fileName, MemoryOwner<byte> buffer)
        {
            FileName = fileName;
            _buffer = buffer;
            Length = buffer.Length;
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

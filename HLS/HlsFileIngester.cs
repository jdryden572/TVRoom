//using Microsoft.Toolkit.HighPerformance.Buffers;
//using System.Diagnostics;
//using System.Diagnostics.CodeAnalysis;
//using System.IO.Pipelines;
//using System.Reactive.Linq;
//using System.Reactive.Subjects;
//using System.Text;
//using System.Text.RegularExpressions;
//using System.Threading.Channels;
//using TVRoom.Broadcast;

//namespace TVRoom.HLS
//{
//    public sealed class HlsFileIngester : IDisposable
//    {
//        private readonly Subject<HlsMasterPlaylist> _masterPlaylists = new();
//        private readonly Subject<HlsVariantPlaylist> _variantPlaylists = new();
//        private readonly Subject<HlsSegment> _segments = new();

//        private readonly Channel<IngestFile> _channel = Channel.CreateBounded<IngestFile>(new BoundedChannelOptions(50));

//        public HlsFileIngester()
//        {
//            _ = Task.Run()
//        }

//        public async Task IngestStreamFileAsync(string fileName, PipeReader fileContentsReader)
//        {
//            var fileType = fileName switch
//            {
//                "master.m3u8" => HlsFileType.MasterPlaylist,
//                "live.m3u8" => HlsFileType.Playlist,
//                _ => HlsFileType.Segment,
//            };

//            if (fileType == HlsFileType.Segment && Path.GetExtension(fileName) != ".ts")
//            {
//                throw new ArgumentException("Unrecognized file extension in HLS file!", nameof(fileName));
//            }

//            var fileContents = await fileContentsReader.PooledReadToEndAsync();
//            await _channel.Writer.WriteAsync(new IngestFile(fileName, fileType, fileContents));
//        }

//        private async Task ListenForFiles()
//        {
//            await foreach (var file in _channel.Reader.ReadAllAsync())
//            {
//                switch (file.FileType)
//                {
//                    case HlsFileType.MasterPlaylist:
//                        var contents = Encoding.UTF8.GetString(file.FileContents.Span);

//                }
//            }
//        }

//        public void Dispose()
//        {
//            _masterPlaylists.OnCompleted();
//            _variantPlaylists.OnCompleted();
//            _segments.OnCompleted();
//        }

//        public IObservable<HlsMasterPlaylist> MasterPlaylists => _masterPlaylists.AsObservable();
//        public IObservable<HlsVariantPlaylist> VariantPlaylists => _variantPlaylists.AsObservable();
//        public IObservable<HlsSegment> Segments => _segments.AsObservable();

//        private sealed record IngestFile(string FileName, HlsFileType FileType, MemoryOwner<byte> FileContents);
//    }
    
//    public interface IHlsFile
//    {
//        IResult GetResult();
//    }

//    public sealed class HlsMasterPlaylist : IHlsFile
//    {
//        private readonly string _contents;

//        public HlsMasterPlaylist(string contents)
//        {
//            _contents = contents;
//        }

//        public IResult GetResult()
//        {
//            return Results.Text(_contents, contentType: "audio/mpegurl");
//        }
//    }

//    public record struct HlsSegmentReference(string FileName, double Duration);

//    public sealed class HlsVariantPlaylist : IHlsFile
//    {
//        public HlsVariantPlaylist(int hlsVersion, long targetDuration, HlsSegmentReference[] segmentReferences)
//        {
//            HlsVersion = hlsVersion;
//            TargetDuration = targetDuration;
//            SegmentReferences = segmentReferences;
//        }

//        public static bool TryParse(string payload, [MaybeNullWhen(false)] out HlsVariantPlaylist parsed)
//        {
//            parsed = null;

//            var versionMatch = VersionRegex().Match(payload);
//            if (!versionMatch.Success)
//            {
//                return false;
//            }

//            versionMatch.
//        }

//        public int HlsVersion { get; }
//        public long TargetDuration { get; }
//        public HlsSegmentReference[] SegmentReferences { get; }

//        public IResult GetResult()
//        {
//            throw new NotImplementedException();
//        }
//    }

//    public sealed class HlsSegment : IHlsFile, IDisposable
//    {
//        private readonly object _lock = new();

//        // Synchronize access to these with the lock
//        private MemoryOwner<byte>? _buffer;
//        private int _refCount;
//        private bool _disposed;

//        public string FileName { get; }
//        public int Length { get; }

//        public static async Task<HlsSegment> ReadAsync(string fileName, PipeReader fileContents)
//        {
//            var buffer = await fileContents.PooledReadToEndAsync();
//            return new HlsSegment(fileName, buffer);
//        }

//        private HlsSegment(string fileName, MemoryOwner<byte> buffer)
//        {
//            FileName = fileName;
//            _buffer = buffer;
//            Length = buffer.Length;
//        }

//        public IResult GetResult()
//        {
//            ReadOnlyMemory<byte> memory;
//            lock (_lock)
//            {
//                if (_disposed || _buffer is null)
//                {
//                    throw new ObjectDisposedException(FileName);
//                }

//                _refCount++;
//                memory = _buffer.Memory;
//            }

//            return new HlsSegmentResult(this, memory);
//        }

//        public void Dispose()
//        {
//            lock (_lock)
//            {
//                _disposed = true;
//            }

//            ReturnBufferIfDisposedAndRefCountZero();
//        }

//#if DEBUG
//        ~HlsSegment()
//        {
//            Debug.Fail($"HlsSegement is being finalized! '{FileName}'");
//        }
//#endif

//        private void ReaderDisposed()
//        {
//            lock (_lock)
//            {
//                _refCount--;
//            }

//            ReturnBufferIfDisposedAndRefCountZero();
//        }

//        private void ReturnBufferIfDisposedAndRefCountZero()
//        {
//            MemoryOwner<byte>? bufferToReturn = null;
//            lock (_lock)
//            {
//                if (_disposed && _refCount == 0)
//                {
//                    bufferToReturn = _buffer;
//                    _buffer = null;
//                }
//            }

//            if (bufferToReturn is not null)
//            {
//                GC.SuppressFinalize(this);
//                bufferToReturn.Dispose();
//            }
//        }


//        private sealed class HlsSegmentResult : IResult
//        {
//            private readonly HlsSegment _hlsSegment;
//            private readonly ReadOnlyMemory<byte> _buffer;
//            private bool _finished;

//            public HlsSegmentResult(HlsSegment hlsSegment, ReadOnlyMemory<byte> buffer)
//            {
//                _hlsSegment = hlsSegment;
//                _buffer = buffer;
//            }

//            public async Task ExecuteAsync(HttpContext httpContext)
//            {
//                if (_finished)
//                {
//                    throw new InvalidOperationException("Cannot execute HlsSegmentResult more than once!");
//                }

//                _finished = true;

//                httpContext.Response.Headers.ContentType = "application/octet-stream";

//                try
//                {
//                    await httpContext.Response.BodyWriter.WriteAsync(_buffer);
//                }
//                finally
//                {
//                    _hlsSegment.ReaderDisposed();
//                }
//            }
//        }
//    }
//}

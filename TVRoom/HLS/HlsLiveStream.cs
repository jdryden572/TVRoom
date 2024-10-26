using System.Buffers;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace TVRoom.HLS
{
    public sealed class HlsLiveStream : IDisposable
    {
        private readonly string _channelUrl;
        private readonly HlsTranscodeManager _transcodeManager;
        private readonly ILogger _logger;
        private readonly HlsConfiguration _hlsConfiguration;
        private readonly BehaviorSubject<HlsStreamState?> _streamState = new(null);
        private HlsTranscode? _currentTranscode;
        private IDisposable? _unsubscribeSegments;

        private HlsStreamState? CurrentStream => _streamState.Value;

        public HlsLiveStream(string channelUrl, HlsTranscodeManager transcodeManager, ILogger logger, HlsConfiguration hlsConfiguration)
        {
            _channelUrl = channelUrl;
            _transcodeManager = transcodeManager;
            _logger = logger;
            _hlsConfiguration = hlsConfiguration;
            var debugOutput = _transcodeOutputs.Switch().Publish();
            debugOutput.Connect();
            DebugOutput = debugOutput;
        }

        private readonly Subject<IObservable<string>> _transcodeOutputs = new();

        public IObservable<string> DebugOutput { get; }

        public async Task StartAsync()
        {
            if (_currentTranscode is not null)
            {
                return;
            }

            var transcode = await _transcodeManager.CreateTranscode(_channelUrl, _logger);
            _unsubscribeSegments = transcode.FileIngester.StreamSegments
                .Select(segmentInfo => HlsStreamState.GetNextState(CurrentStream, segmentInfo, _hlsConfiguration.HlsListSize))
                .Subscribe(_streamState);
            _transcodeOutputs.OnNext(transcode.FFmpegProcess.FFmpegOutput);
            transcode.Start();
            _currentTranscode = transcode;
        }

        public async Task RestartAsync()
        {
            if (_currentTranscode is not null)
            {
                await _currentTranscode.StopAsync();
                _currentTranscode.Dispose();
                _currentTranscode = null;
                _unsubscribeSegments?.Dispose();
                _unsubscribeSegments = null;

                var stream = CurrentStream;
                if (stream is not null)
                {
                    _streamState.OnNext(HlsStreamState.GetNextStateForDiscontinuity(stream, _hlsConfiguration.HlsListSize));
                }

                await StartAsync();
            }
        }

        public async Task StopAsync()
        {
            if (_currentTranscode is not null)
            {
                await _currentTranscode.StopAsync();
            }
        }

        public IResult GetMasterPlaylist()
        {
            var stream = CurrentStream;
            return stream is not null ? new PlaylistResult(stream.WriteMasterPlaylist) : Results.NotFound();
        }

        public IResult GetPlaylist()
        {
            var stream = CurrentStream;
            return stream is not null ? new PlaylistResult(stream.WriteStreamPlaylist) : Results.NotFound();
        }

        public IResult GetSegment(int index)
        {
            var stream = CurrentStream;
            return stream?.GetSegmentResult(index) ?? Results.NotFound();
        }


        public void Dispose()
        {
            _currentTranscode?.Dispose();
        }
    }

    public sealed record PlaylistResult(Action<IBufferWriter<byte>> write) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.ContentType = "audio/mpegurl";
            write(httpContext.Response.BodyWriter);
            await httpContext.Response.BodyWriter.FlushAsync();
        }
    }
}

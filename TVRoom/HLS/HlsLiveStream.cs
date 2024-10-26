using System.Reactive.Linq;
using System.Reactive.Subjects;
using TVRoom.Transcode;

namespace TVRoom.HLS
{
    public sealed class HlsLiveStream : IDisposable
    {
        private readonly BehaviorSubject<IObservable<HlsSegmentInfo>> _sources;
        private readonly BehaviorSubject<HlsStreamState> _streamStates;
        private readonly IDisposable _unsubscribeToSources;

        private HlsStreamState StreamState => _streamStates.Value;

        public HlsLiveStream(IObservable<HlsSegmentInfo> source, HlsConfiguration hlsConfig)
        {
            _sources = new(source);
            _streamStates = new(new HlsStreamNotReady(hlsConfig.HlsListSize));

            _unsubscribeToSources = _sources.Switch().Subscribe(segmentInfo =>
            {
                var newState = _streamStates.Value.WithNewSegment(segmentInfo);
                _streamStates.OnNext(newState);
            });
        }

        public void SetNewSource(IObservable<HlsSegmentInfo> source)
        {
            _streamStates.OnNext(_streamStates.Value.WithNewDiscontinuity());
            _sources.OnNext(source);
        }

        public IResult GetMasterPlaylist() => StreamState.GetMasterPlaylist();

        public IResult GetPlaylist() => StreamState.GetPlaylist();

        public IResult GetSegment(int index) => StreamState.GetSegment(index);

        public void Dispose()
        {
            // Add new subscriber that will dispose of any further segments from the source.
            _sources.SelectMany(s => s).Subscribe(segmentInfo => segmentInfo.Payload.Dispose());

            // Stop our subscription
            _unsubscribeToSources.Dispose();

            // Clean up any segments in our stream state
            StreamState.DisposeAllSegments();
        }
    }
}

using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using TVRoom.Configuration;

namespace TVRoom.HLS
{
    public sealed class MergedHlsLiveStream : IDisposable
    {
        private readonly BehaviorSubject<IObservable<HlsSegmentInfo>> _sources;
        private readonly BehaviorSubject<HlsStreamState> _streamStates;
        private readonly IDisposable _unsubscribeToSources;

        private HlsStreamState StreamState => _streamStates.Value;

        public MergedHlsLiveStream(IObservable<HlsSegmentInfo> source, HlsConfiguration hlsConfig)
        {
            _sources = new(source);
            _streamStates = new(new HlsStreamNotReady(hlsConfig.HlsListSize));

            _unsubscribeToSources = _sources.Switch().Subscribe(segmentInfo =>
            {
                var newState = _streamStates.Value.WithNewSegment(segmentInfo);
                _streamStates.OnNext(newState);
            });

            Ready = _streamStates.Skip(1).Take(hlsConfig.HlsPlaylistReadyCount).ToTask();
        }

        public Task Ready { get; }

        public bool IsReady => Ready.IsCompleted;

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
            // Stop our subscription
            _unsubscribeToSources.Dispose();

            _sources.OnCompleted();
            _sources.Dispose();

            _streamStates.OnCompleted();
            _streamStates.Dispose();
        }
    }
}

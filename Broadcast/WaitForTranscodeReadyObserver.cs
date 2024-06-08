using System.Text.RegularExpressions;

namespace LivingRoom.Broadcast
{
    internal sealed class WaitForTranscodeReadyObserver : IObserver<string>
    {
        private static readonly Regex _writingSegment = new Regex(@"^\[hls @ \S+\] Opening '.*?\.ts'", RegexOptions.Compiled);
        private readonly TaskCompletionSource _taskCompletionSource = new();
        private readonly int _requiredSegmentCount;
        private int _segmentCount = 0;

        public WaitForTranscodeReadyObserver(int requiredSegmentCount) => _requiredSegmentCount = requiredSegmentCount;

        public Task TranscodeReady => _taskCompletionSource.Task;

        public void OnCompleted() => _taskCompletionSource.SetException(
            new Exception($"FFmpegProcess stopped before writing {_requiredSegmentCount} segments"));

        public void OnError(Exception error) => _taskCompletionSource.SetException(error);

        public void OnNext(string value)
        {
            if (_writingSegment.IsMatch(value))
            {
                _segmentCount++;
            }

            if (_segmentCount >= _requiredSegmentCount)
            {
                _taskCompletionSource.SetResult();
            }
        }
    }
}

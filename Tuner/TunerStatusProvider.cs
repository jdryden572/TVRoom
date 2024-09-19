using TVRoom.Broadcast;

namespace TVRoom.Tuner
{
    public sealed class TunerStatusProvider : BaseObservable<TunerStatus[]>, IDisposable
    {
        private readonly object _lock = new();
        private readonly TunerClient _tunerClient;

        private CancellationTokenSource? _cts;

        public TunerStatusProvider(TunerClient tunerClient)
        {
            _tunerClient = tunerClient;
        }

        public void Dispose()
        {
            Complete();
            _cts?.Cancel();
            _cts?.Dispose();
        }

        protected override void OnSubscriberCountChange(int count)
        {
            lock (_lock)
            {
                if (count > 0 && _cts is null)
                {
                    _cts = new();
                    _ = Task.Run(() => StartAsync(_cts.Token));
                }
                else if (count == 0 && _cts is not null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }

        private async Task StartAsync(CancellationToken cancellation)
        {
            using var ticker = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await ticker.WaitForNextTickAsync(cancellation);
                    var statuses = await _tunerClient.GetTunerStatusesAsync(cancellation);
                    Next(statuses);
                }
                catch (Exception ex)
                {
                    Error(ex);
                }
            }
        }
    }
}

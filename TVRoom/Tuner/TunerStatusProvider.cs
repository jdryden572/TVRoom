using System.Reactive.Linq;

namespace TVRoom.Tuner
{
    public sealed class TunerStatusProvider 
    {
        private readonly TunerClient _tunerClient;
        private readonly ILogger _logger;

        public TunerStatusProvider(TunerClient tunerClient, ILogger<TunerStatusProvider> logger)
        {
            _tunerClient = tunerClient;
            _logger = logger;

            Statuses = Observable.Create<TunerStatus[]>(GetStatusesPeriodicallyAsync)
                .Publish()
                .RefCount();
        }

        public IObservable<TunerStatus[]> Statuses { get; }

        private async Task GetStatusesPeriodicallyAsync(IObserver<TunerStatus[]> obs, CancellationToken cancellation)
        {
            _logger.LogInformation("Starting to fetch tuner status periodically");
            using var ticker = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await ticker.WaitForNextTickAsync(cancellation);
                    var statuses = await _tunerClient.GetTunerStatusesAsync(cancellation);
                    obs.OnNext(statuses);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching tuner status");
                }
            }

            _logger.LogInformation("Stopping tuner status fetching");
        }
    }
}

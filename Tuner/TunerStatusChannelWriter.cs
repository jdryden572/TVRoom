using System.Threading.Channels;

namespace TVRoom.Tuner
{
    public class TunerStatusChannelWriter
    {
        private readonly ChannelWriter<TunerStatus[]> _channelWriter;
        private readonly TunerClient _tunerClient;

        private TunerStatusChannelWriter(ChannelWriter<TunerStatus[]> channelWriter, TunerClient tunerClient)
        {
            _channelWriter = channelWriter;
            _tunerClient = tunerClient;
        }

        public static ChannelReader<TunerStatus[]> CreateReader(TunerClient tunerClient, CancellationToken cancellation)
        {
            var channel = Channel.CreateUnbounded<TunerStatus[]>();
            var statusWriter = new TunerStatusChannelWriter(channel.Writer, tunerClient);
            Task.Run(() => statusWriter.StartAsync(cancellation));
            return channel.Reader;
        }

        public async Task StartAsync(CancellationToken cancellation)
        {
            using var ticker = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await ticker.WaitForNextTickAsync(cancellation);
                    var statuses = await _tunerClient.GetTunerStatusesAsync(cancellation);
                    await _channelWriter.WriteAsync(statuses, cancellation);
                }
                catch { }
            }

            _channelWriter.Complete();
        }
    }
}

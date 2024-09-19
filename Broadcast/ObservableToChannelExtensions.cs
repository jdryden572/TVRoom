using System.Threading.Channels;

namespace TVRoom.Broadcast
{
    public static class ObservableToChannelExtensions
    {
        private static readonly BoundedChannelOptions _debugOutputChannelOptions =
            new BoundedChannelOptions(200)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            };

        public static ChannelReader<T> AsChannelReader<T>(this IObservable<T> source, CancellationToken unsubscribe)
        {
            var channel = Channel.CreateBounded<T>(_debugOutputChannelOptions);
            var (writer, reader) = (channel.Writer, channel.Reader);

            var unsubscriber = source.Subscribe(
                val => writer.TryWrite(val),
                ex => writer.TryComplete(ex),
                () => writer.TryComplete());

            unsubscribe.Register(() =>
            {
                unsubscriber.Dispose();
                writer.TryComplete();
            });

            return reader;
        }
    }
}

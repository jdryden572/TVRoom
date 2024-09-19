using System.Threading.Channels;

namespace TVRoom.Broadcast
{
    public static class ChannelHelper
    {
        private static readonly BoundedChannelOptions _debugOutputChannelOptions =
            new BoundedChannelOptions(200)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            };

        public static ChannelReader<T> CreateReader<T>(IObservable<T> observable, CancellationToken unsubscribe)
        {
            var channel = Channel.CreateBounded<T>(_debugOutputChannelOptions);
            var observer = new WriteToChannelObserver<T>(channel.Writer);
            var unsubscriber = observable.Subscribe(observer);

            unsubscribe.Register(() =>
            {
                unsubscriber.Dispose();
                observer.Dispose();
            });

            return channel.Reader;
        }
    }
}

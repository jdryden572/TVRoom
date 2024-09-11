using System.Threading.Channels;

namespace TVRoom.Broadcast
{
    public static class ChannelHelpers
    {
        private static readonly BoundedChannelOptions _channelOptions =
            new BoundedChannelOptions(200)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            };

        public static ChannelReader<T> GetChannelReader<T>(IObservable<T> observable, CancellationToken unsubscribe)
        {
            var channel = Channel.CreateBounded<T>(_channelOptions);
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

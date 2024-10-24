using System.Threading.Channels;

namespace TVRoom.Broadcast
{
    internal sealed class WriteToChannelObserver<T> : IObserver<T>, IDisposable
    {
        private readonly ChannelWriter<T> _writer;

        public WriteToChannelObserver(ChannelWriter<T> writer) => _writer = writer;

        public void OnCompleted() => Dispose();

        public void OnError(Exception error)
        {
            if (_writer is ChannelWriter<string> stringWriter)
            {
                stringWriter.TryWrite(error.ToString());
            }
        }

        public void OnNext(T value) => _writer.TryWrite(value);

        public void Dispose()
        {
            try
            {
                _writer.Complete();
            }
            catch (InvalidOperationException)
            {
                // This channel writer has already been completed
            }
        }
    }
}

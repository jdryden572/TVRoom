using System.Threading.Channels;

namespace LivingRoom.Broadcast
{
    internal sealed class WriteToChannelObserver : IObserver<string>, IDisposable
    {
        private readonly ChannelWriter<string> _writer;

        public WriteToChannelObserver(ChannelWriter<string> writer) => _writer = writer;

        public void OnCompleted() => Dispose();

        public void OnError(Exception error)
        {
            _writer.TryWrite(error.ToString());
        }

        public void OnNext(string value) => _writer.TryWrite(value);

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
